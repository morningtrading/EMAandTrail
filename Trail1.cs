#region Using declarations
// System libraries for basic functionality
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
// NinjaTrader specific libraries
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // Enumeration defining the available trading directions for the strategy
    public enum TradingDirection
    {
        Both,       // Allow both long and short trades
        LongOnly,   // Only allow long (buy) trades
        ShortOnly   // Only allow short (sell) trades
    }
    
    // Main strategy class Trail1 from NinjaTrader's Strategy base class
    public class Trail1 : Strategy
    {
        // Private indicator instances - these will hold our technical indicators
        private EMA FastEMA;   // Fast EMA indicator (9 periods)
        private EMA SlowEMA;  // Slow EMA indicator (41 periods)
        private ATR atr;    // Average True Range indicator for volatility measurement
        
        // Stop loss tracking variables
        private double longStopLoss = 0;        // Current stop loss level for long positions
        private double shortStopLoss = 0;       // Current stop loss level for short positions
        private bool trailingActivated = false; // Flag to track if trailing stop is active
        private bool breakevenActivated = false; // Flag to track if breakeven protection is active
        
        // Drawing object name for stop loss line on chart
        private string stopLossLineName = "StopLossLine";
        
        // Dashboard statistics variables - track overall performance
        private int totalTrades = 0;           // Total number of completed trades
        private int winningTrades = 0;         // Number of profitable trades
        private int losingTrades = 0;          // Number of losing trades
        private double totalPnL = 0;           // Total profit/loss for the session
        private double grossProfit = 0;        // Total profit from winning trades
        private double grossLoss = 0;          // Total loss from losing trades
        private double largestWin = 0;         // Largest single winning trade
        private double largestLoss = 0;        // Largest single losing trade
        private double currentDrawdown = 0;    // Current drawdown from peak
        private double maxDrawdown = 0;        // Maximum drawdown experienced
        private double peakPnL = 0;           // Peak profit/loss level reached
        private List<double> tradePnLs = new List<double>(); // List storing all individual trade P&Ls
        private DateTime sessionStartTime;     // Time when strategy session started
        
        // Detailed trade statistics - separate tracking for long vs short trades
        private int longTrades = 0;           // Total long trades
        private int shortTrades = 0;          // Total short trades
        private int longWins = 0;             // Winning long trades
        private int shortWins = 0;            // Winning short trades
        private int longLosses = 0;           // Losing long trades
        private int shortLosses = 0;          // Losing short trades
        private double longPnL = 0;           // Total P&L from long trades
        private double shortPnL = 0;          // Total P&L from short trades
        private double largestWinLong = 0;    // Largest winning long trade
        private double largestLossLong = 0;   // Largest losing long trade
        private double largestWinShort = 0;   // Largest winning short trade
        private double largestLossShort = 0;  // Largest losing short trade
        private int consecutiveWins = 0;      // Current streak of winning trades
        private int consecutiveLosses = 0;    // Current streak of losing trades
        private int maxConsecutiveWins = 0;   // Maximum consecutive wins achieved
        private int maxConsecutiveLosses = 0; // Maximum consecutive losses experienced
        
        // Simple trade tracking for manual P&L calculation
        private double entryPrice = 0;        // Price at which current position was entered
        private bool isLongPosition = false;  // Flag indicating if current position is long
        
        // Our own position tracking to avoid NinjaTrader Position.MarketPosition issues
        private MarketPosition ourPositionState = MarketPosition.Flat;  // Our tracked position state
        private int ourPositionQuantity = 0;                           // Our tracked position quantity
        private DateTime lastPositionUpdate = DateTime.MinValue;        // Last time we updated our position
        
        // Chart display object names for various text displays
        private string statusTextName = "StatusText";           // Status display object name
        private string pnlTextName = "PnLText";                // P&L display object name
        private string dashboardTextName = "DashboardText";     // Main dashboard object name
        private string timeFilterTextName = "TimeFilterText";  // Time filter status object name
        private string directionTextName = "DirectionText";    // Trading direction object name
        private string pnlHistoryTextName = "PnLHistoryText";  // P&L history dashboard object name
        
        // Override method called when strategy state changes (SetDefaults, Configure, Active, etc.)
        protected override void OnStateChange()
        {
            // State.SetDefaults: Initialize default parameter values and strategy properties
            if (State == State.SetDefaults)
            {
                // Basic strategy information
                Description = @"NQ Strategy with configurable EMA crossover and trailing stop"; // Strategy description
                Name = "Trail1";                          // Strategy name as it appears in NinjaTrader
                Calculate = Calculate.OnBarClose;                   // Calculate on bar close for reliable signals
                EntriesPerDirection = 1;                           // Maximum 1 position per direction (long/short)
                EntryHandling = EntryHandling.AllEntries;          // Allow all entry orders to be processed
                IsExitOnSessionCloseStrategy = true;               // Automatically exit positions at session close
                ExitOnSessionCloseSeconds = 30;                    // Exit 30 seconds before session close
                IsFillLimitOnTouch = false;                        // Standard limit order fill behavior
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix; // Historical data lookback limit
                OrderFillResolution = OrderFillResolution.Standard; // Standard order fill resolution
                Slippage = 0;                                      // No slippage simulation
                StartBehavior = StartBehavior.WaitUntilFlat;       // Wait for flat position before starting
                TimeInForce = TimeInForce.Gtc;                     // Good Till Cancelled order duration
                TraceOrders = false;                               // Don't trace order information to output
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose; // Stop strategy on errors
                StopTargetHandling = StopTargetHandling.PerEntryExecution; // Handle stops per entry execution
                BarsRequiredToTrade = 50;                          // Minimum bars needed before trading
                
                // Strategy parameter default values
                TrailingStopPoints = 20;                           // Base trailing stop distance in points
                AtrMultiplier = 2.5;                              // Multiplier for ATR-based stop distance
                ProfitTriggerPoints = 5;                          // Points needed to activate breakeven protection (reduced for faster trailing)
                ProgressiveTighteningRate = 0.2;                  // Rate of stop tightening as profit increases (faster acceleration)
                Quantity = 1;                                      // Number of contracts to trade
                EmaPeriod1 = 9;                                   // Fast EMA period
                EmaPeriod2 = 41;                                  // Slow EMA period
                
                // Time filter settings (New York timezone)
                StartTime = DateTime.Parse("08:30", System.Globalization.CultureInfo.InvariantCulture); // Trading start time
                EndTime = DateTime.Parse("15:25", System.Globalization.CultureInfo.InvariantCulture);   // Trading end time
                UseTimeFilter = true;                             // Enable time-based trading filter
                
                // Trading direction default setting
                Direction = TradingDirection.Both;                // Allow both long and short trades
                
                // Visual display default settings
                StopLossColor = Brushes.Cyan;                     // Cyan color for stop loss line (more visible)
                LineThickness = 5;                                // Stop loss line thickness
            }
            // State.Configure: Initialize indicators and configure strategy
            else if (State == State.Configure)
            {
                // Initialize technical indicators with specified periods
                FastEMA = EMA(EmaPeriod1);                           // Create fast EMA indicator
                SlowEMA = EMA(EmaPeriod2);                          // Create slow EMA indicator
                atr = ATR(14);                                    // Create ATR indicator with 14-period default
                
                // Add indicators to chart display with thick lines
                AddChartIndicator(FastEMA);                          // Add fast EMA to chart
                AddChartIndicator(SlowEMA);                         // Add slow EMA to chart
                
                // Configure EMA visual appearance with thick, colored lines
                FastEMA.Plots[0].Brush = Brushes.Green;              // Set fast EMA to green color
                FastEMA.Plots[0].Width = 6;                          // Set fast EMA line width to 6 pixels
                FastEMA.Plots[0].PlotStyle = PlotStyle.Line;         // Use solid line style for fast EMA
                
                SlowEMA.Plots[0].Brush = Brushes.Red;               // Set slow EMA to red color
                SlowEMA.Plots[0].Width = 6;                         // Set slow EMA line width to 6 pixels
                SlowEMA.Plots[0].PlotStyle = PlotStyle.Line;        // Use solid line style for slow EMA
                
                // Initialize dashboard tracking variables
                sessionStartTime = DateTime.Now;                  // Record strategy start time
                totalPnL = 0;                                     // Reset total profit/loss
                totalTrades = 0;                                  // Reset trade counter
                tradePnLs.Clear();                                // Clear trade P&L history list
                
                // Initialize our position tracking
                ourPositionState = MarketPosition.Flat;           // Start with flat position
                ourPositionQuantity = 0;                          // Start with zero quantity
                lastPositionUpdate = DateTime.Now;               // Record initialization time
            }
            // State.Terminated: Clean up when strategy stops
            else if (State == State.Terminated)
            {
                // Remove chart objects to prevent memory leaks
                RemoveDrawObject(dashboardTextName);              // Remove dashboard display
                RemoveDrawObject(stopLossLineName);               // Remove stop loss line
                RemoveDrawObject(pnlHistoryTextName);             // Remove P&L history dashboard
            }
        }

        // Override method called on each new bar - main strategy logic
        protected override void OnBarUpdate()
        {
            // Ensure we have enough historical data before proceeding
            if (CurrentBar < Math.Max(EmaPeriod1, EmaPeriod2) || CurrentBar < 14)
                return; // Exit if insufficient data for indicators

            try // Wrap main logic in try-catch for error handling
            {
                // Time filter logic - determine if trading is allowed based on time
                bool tradingAllowed = true;                       // Default to allowing trading
                string timeFilterStatus = "";                     // Status message for time filter
                
                // Check if time filter is enabled
                if (UseTimeFilter)
                {
                    // Convert computer time to New York time (subtract 6 hours for EST)
                    TimeSpan currentTime = Time[0].TimeOfDay.Add(TimeSpan.FromHours(-6));
                    TimeSpan startTime = StartTime.TimeOfDay;     // Get start time from parameter
                    TimeSpan endTime = EndTime.TimeOfDay;         // Get end time from parameter
                    
                    // Handle negative time (would indicate previous day)
                    if (currentTime < TimeSpan.Zero)
                        currentTime = currentTime.Add(TimeSpan.FromHours(24)); // Add 24 hours to get correct time
                    
                    // Check if current time is outside allowed trading hours
                    if (currentTime < startTime || currentTime > endTime)
                    {
                        tradingAllowed = false;                   // Disable trading outside hours
                        // Create status message for closed market
                        timeFilterStatus = $"TRADING CLOSED - Outside NY hours ({startTime:hh\\:mm} - {endTime:hh\\:mm}) | NY Time: {currentTime:hh\\:mm}";
                    }
                    else
                    {
                        // Create status message for open market
                        timeFilterStatus = $"TRADING OPEN - Within NY hours ({startTime:hh\\:mm} - {endTime:hh\\:mm}) | NY Time: {currentTime:hh\\:mm}";
                    }
                }
                else
                {
                    // Time filter disabled - create appropriate status message
                    timeFilterStatus = "TIME FILTER DISABLED - 24/7 Trading";
                }
                
                // If trading not allowed due to time filter, only process existing positions
                if (!tradingAllowed && UseTimeFilter)
                {
                    // Process exits and trailing stops for existing positions only
                    ProcessExistingPositions();                   // Handle existing trades
                    UpdateChartDisplay();                         // Update dashboard display
                    return;                                       // Exit without looking for new entries
                }
                
                // EMA crossover signal detection
                bool bullishCrossover = CrossAbove(FastEMA, SlowEMA, 1); // Fast EMA crosses above slow EMA (buy signal)
                bool bearishCrossover = CrossBelow(FastEMA, SlowEMA, 1);  // Fast EMA crosses below slow EMA (sell signal)
                
                // Debug: Print EMA values and crossover status only when crossover occurs
                if (bullishCrossover || bearishCrossover)
                {
                    Print($"CROSSOVER DETECTED - Bar {CurrentBar}: FastEMA={FastEMA[0]:F2}, SlowEMA={SlowEMA[0]:F2}, Bull={bullishCrossover}, Bear={bearishCrossover}, TradingAllowed={tradingAllowed}");
                    
                    // Draw crossover signal icons on chart - default colors for signal only
                    if (bullishCrossover)
                    {
                        string crossoverIconName = $"BullCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BullText_{Time[0].Ticks}";
                        // Draw arrow closer to price action with autoscale enabled
                        Draw.ArrowUp(this, crossoverIconName, true, 0, Low[0] - (3 * TickSize), Brushes.Blue);
                        // Draw text label
                        Draw.Text(this, crossoverTextName, "BULL\nCROSS", 0, Low[0] - (8 * TickSize), Brushes.Blue);
                        Print($"Drawing Bull Crossover Arrow at bar {CurrentBar}, time {Time[0]}, price {Low[0] - (3 * TickSize):F2}");
                    }
                    
                    if (bearishCrossover)
                    {
                        string crossoverIconName = $"BearCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BearText_{Time[0].Ticks}";
                        // Draw arrow closer to price action with autoscale enabled
                        Draw.ArrowDown(this, crossoverIconName, true, 0, High[0] + (3 * TickSize), Brushes.Purple);
                        // Draw text label
                        Draw.Text(this, crossoverTextName, "BEAR\nCROSS", 0, High[0] + (8 * TickSize), Brushes.Purple);
                        Print($"Drawing Bear Crossover Arrow at bar {CurrentBar}, time {Time[0]}, price {High[0] + (3 * TickSize):F2}");
                    }
                }
                
                // Long entry logic - simplified without rejection tracking
                if (bullishCrossover)
                {
                    bool canEnterLong = true;
                    
                    // FORCE SYNCHRONIZATION at start of every crossover check
                    if ((Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0) && 
                        (ourPositionState != MarketPosition.Flat || ourPositionQuantity != 0))
                    {
                        Print($"FORCING SYNC: NT is flat but our tracking shows {ourPositionState} with qty {ourPositionQuantity} - correcting our tracking");
                        ourPositionState = MarketPosition.Flat;
                        ourPositionQuantity = 0;
                        lastPositionUpdate = Time[0];
                    }
                    
                    // Check if BOTH systems show flat position
                    bool ntIsFlat = (Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0);
                    bool ourTrackingIsFlat = (ourPositionState == MarketPosition.Flat && ourPositionQuantity == 0);
                    bool isActuallyFlat = ntIsFlat && ourTrackingIsFlat;
                    
                    // Check if systems agree on position state
                    bool systemsAgree = (Position.MarketPosition == ourPositionState && Position.Quantity == ourPositionQuantity);
                    
                    // Check each condition for entry
                    if (!systemsAgree)
                    {
                        canEnterLong = false;
                    }
                    else if (!isActuallyFlat && Position.MarketPosition == MarketPosition.Long)
                    {
                        canEnterLong = false;
                    }
                    else if (Direction == TradingDirection.ShortOnly)
                    {
                        canEnterLong = false;
                    }
                    else if (!tradingAllowed && UseTimeFilter)
                    {
                        canEnterLong = false;
                    }
                    
                    if (canEnterLong)
                    {
                        EnterLong(Quantity, "Long Entry");           // Place long entry order
                        
                        // Update our own position tracking
                        ourPositionState = MarketPosition.Long;
                        ourPositionQuantity = Quantity;
                        lastPositionUpdate = Time[0];
                        
                        longStopLoss = Close[0] - (TrailingStopPoints * TickSize); // Set initial stop loss
                        trailingActivated = false;                   // Reset trailing stop flag
                        breakevenActivated = false;                  // Reset breakeven protection flag
                        DrawStopLossLine(longStopLoss);              // Draw stop loss line on chart
                        
                        // Remove the trade display icons and text
                        /*
                        // Change crossover icon to bright green to indicate trade taken
                        string crossoverIconName = $"BullCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BullText_{Time[0].Ticks}";
                        Draw.ArrowUp(this, crossoverIconName, true, 0, Low[0] - (3 * TickSize), Brushes.LimeGreen);
                        Draw.Text(this, crossoverTextName, "BULL\nTRADE", 0, Low[0] - (15 * TickSize), Brushes.LimeGreen);
                        Print($"Updated Bull Crossover to TRADE - Green arrow at {Low[0] - (3 * TickSize):F2}");
                        */
                        
                        Print($"LONG ENTRY: Bullish crossover at {Close[0]:F2} - Updated our position tracking to LONG");
                        
                        // Draw visual marker for successful entry
                        string entryMarkerName = $"LongEntry_{CurrentBar}";
                        Draw.TriangleUp(this, entryMarkerName, false, 0, Low[0] - (5 * TickSize), Brushes.Green);
                    }
                }
                
                // Short entry logic - simplified without rejection tracking
                if (bearishCrossover)
                {
                    bool canEnterShort = true;
                    
                    // FORCE SYNCHRONIZATION at start of every crossover check
                    if ((Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0) && 
                        (ourPositionState != MarketPosition.Flat || ourPositionQuantity != 0))
                    {
                        Print($"FORCING SYNC: NT is flat but our tracking shows {ourPositionState} with qty {ourPositionQuantity} - correcting our tracking");
                        ourPositionState = MarketPosition.Flat;
                        ourPositionQuantity = 0;
                        lastPositionUpdate = Time[0];
                    }
                    
                    // Check if BOTH systems show flat position
                    bool ntIsFlat = (Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0);
                    bool ourTrackingIsFlat = (ourPositionState == MarketPosition.Flat && ourPositionQuantity == 0);
                    bool isActuallyFlat = ntIsFlat && ourTrackingIsFlat;
                    
                    // Check if systems agree on position state
                    bool systemsAgree = (Position.MarketPosition == ourPositionState && Position.Quantity == ourPositionQuantity);
                    
                    // Check each condition for entry
                    if (!systemsAgree)
                    {
                        canEnterShort = false;
                    }
                    else if (!isActuallyFlat && Position.MarketPosition == MarketPosition.Short)
                    {
                        canEnterShort = false;
                    }
                    else if (Direction == TradingDirection.LongOnly)
                    {
                        canEnterShort = false;
                    }
                    else if (!tradingAllowed && UseTimeFilter)
                    {
                        canEnterShort = false;
                    }
                    
                    if (canEnterShort)
                    {
                        EnterShort(Quantity, "Short Entry");        // Place short entry order
    
                        // Update our own position tracking
                        ourPositionState = MarketPosition.Short;
                        ourPositionQuantity = Quantity;
                        lastPositionUpdate = Time[0];
                        
                        shortStopLoss = Close[0] + (TrailingStopPoints * TickSize); // Set initial stop loss
                        trailingActivated = false;                  // Reset trailing stop flag
                        breakevenActivated = false;                 // Reset breakeven protection flag
                        DrawStopLossLine(shortStopLoss);            // Draw stop loss line on chart
                        
                        // Remove the trade display icons and text
                        /*
                        // Change crossover icon to bright red to indicate trade taken
                        string crossoverIconName = $"BearCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BearText_{Time[0].Ticks}";
                        Draw.ArrowDown(this, crossoverIconName, true, 0, High[0] + (3 * TickSize), Brushes.Red);
                        Draw.Text(this, crossoverTextName, "BEAR\nTRADE", 0, High[0] + (15 * TickSize), Brushes.Red);
                        Print($"Updated Bear Crossover to TRADE - Red arrow at {High[0] + (3 * TickSize):F2}");
                        */
                        
                        Print($"SHORT ENTRY: Bearish crossover at {Close[0]:F2} - Updated our position tracking to SHORT");
                        
                        // Draw visual marker for successful entry
                        string entryMarkerName = $"ShortEntry_{CurrentBar}";
                        Draw.TriangleDown(this, entryMarkerName, false, 0, High[0] + (5 * TickSize), Brushes.Red);
                    }
                }
                
                // Process trailing stop for active long position
                if (ourPositionState == MarketPosition.Long)
                {
                    ProcessLongTrailingStop();                  // Handle long position trailing stop logic
                }
                
                // Process trailing stop for active short position
                if (ourPositionState == MarketPosition.Short)
                {
                    ProcessShortTrailingStop();                 // Handle short position trailing stop logic
                }
                
                // Exit on opposite crossover signal - check BOTH tracking systems
                if ((ourPositionState == MarketPosition.Long || Position.MarketPosition == MarketPosition.Long) && bearishCrossover)
                {
                    Print($"BEARISH CROSSOVER EXIT: Our={ourPositionState}, NT={Position.MarketPosition} - Exiting Long");
                    ExitLong("Long Exit Signal", "Long Entry");  // Exit long position on bearish signal
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"LONG EXIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                       // Remove stop loss line from chart
                }
                
                if ((ourPositionState == MarketPosition.Short || Position.MarketPosition == MarketPosition.Short) && bullishCrossover)
                {
                    Print($"BULLISH CROSSOVER EXIT: Our={ourPositionState}, NT={Position.MarketPosition} - Exiting Short");
                    ExitShort("Short Exit Signal", "Short Entry"); // Exit short position on bullish signal
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"SHORT EXIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                       // Remove stop loss line from chart
                }
                
                // Update chart display with current information
                UpdateChartDisplay();                           // Refresh dashboard and chart objects
            }
            catch (Exception ex) // Handle any errors that occur
            {
                Print($"Error in OnBarUpdate: {ex.Message}");   // Log error to output window
            }
        }
        
        // Advanced trailing stop logic for long positions with multiple features
        private void ProcessLongTrailingStop()
        {
            try // Wrap in try-catch for error handling
            {
                // Exit if no valid entry price recorded
                if (entryPrice <= 0) return;
                
                // Calculate current profit in points (price difference divided by tick size)
                double currentProfitPoints = (Close[0] - entryPrice) / TickSize;
                
                // Immediate trailing option - start trailing right away (more aggressive)
                // Comment out the breakeven section below if you want immediate trailing
                
                // Breakeven protection - move stop to entry price once profitable enough
                if (!breakevenActivated && currentProfitPoints >= ProfitTriggerPoints)
                {
                    longStopLoss = entryPrice + (2 * TickSize);   // Set stop slightly above entry price
                    breakevenActivated = true;                    // Mark breakeven protection as active
                    trailingActivated = true;                     // Mark trailing stop as active
                    DrawStopLossLine(longStopLoss);              // Draw updated stop line on chart
                    Print($"Long position: Breakeven protection activated at {longStopLoss:F2}"); // Log activation
                    return;                                       // Exit early to allow breakeven to take effect
                }
                
                // Alternative: Immediate trailing (uncomment the lines below and comment out breakeven section above)
                // trailingActivated = true;  // Start trailing immediately
                
                // Progressive tightening - reduce trailing distance as profit increases
                double baseDistance = TrailingStopPoints;         // Start with base trailing stop distance
                if (currentProfitPoints > ProfitTriggerPoints)   // Only tighten after breakeven trigger
                {
                    // Calculate how many profit levels above trigger point we are
                    double profitLevels = (currentProfitPoints - ProfitTriggerPoints) / 5; // Every 5 points = 1 level (faster acceleration)
                    // Calculate reduction amount based on profit levels and tightening rate
                    double reduction = profitLevels * ProgressiveTighteningRate * baseDistance;
                    // Apply reduction but maintain minimum distance (20% of original for more aggressive tightening)
                    baseDistance = Math.Max(baseDistance - reduction, baseDistance * 0.2);
                }
                
                // ATR-based dynamic distance calculation for volatility adaptation
                double atrValue = atr[0];                         // Get current ATR value
                double atrDistance = AtrMultiplier * atrValue / TickSize; // Convert ATR to points using multiplier
                double trailingDistance = Math.Max(baseDistance, atrDistance); // Use larger of progressive or ATR distance
                
                // Calculate new stop level based on current price and trailing distance
                double newStopLevel = Close[0] - (trailingDistance * TickSize);
                
                // Update stop only if new level is higher (more favorable) or if trailing not yet activated
                if (newStopLevel > longStopLoss || !trailingActivated)
                {
                    longStopLoss = newStopLevel;                  // Update stop loss level
                    trailingActivated = true;                     // Mark trailing as active
                    DrawStopLossLine(longStopLoss);              // Draw updated stop line
                }
                
                // Check if stop loss has been hit and exit position
                if (Low[0] <= longStopLoss)
                {
                    ExitLong("Long Stop", "Long Entry");         // Exit long position
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"LONG STOP HIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                        // Remove stop line from chart
                }
            }
            catch (Exception ex) // Handle any errors
            {
                Print($"Error in ProcessLongTrailingStop: {ex.Message}"); // Log error
            }
        }
        
        // Advanced trailing stop logic for short positions with multiple features
        private void ProcessShortTrailingStop()
        {
            try // Wrap in try-catch for error handling
            {
                // Exit if no valid entry price recorded
                if (entryPrice <= 0) return;
                
                // Calculate current profit in points (entry price minus current price for shorts)
                double currentProfitPoints = (entryPrice - Close[0]) / TickSize;
                
                // Immediate trailing option - start trailing right away (more aggressive)
                // Comment out the breakeven section below if you want immediate trailing
                
                // Breakeven protection - move stop to entry price once profitable enough
                if (!breakevenActivated && currentProfitPoints >= ProfitTriggerPoints)
                {
                    shortStopLoss = entryPrice - (2 * TickSize);  // Set stop slightly below entry price
                    breakevenActivated = true;                    // Mark breakeven protection as active
                    trailingActivated = true;                     // Mark trailing stop as active
                    DrawStopLossLine(shortStopLoss);             // Draw updated stop line on chart
                    Print($"Short position: Breakeven protection activated at {shortStopLoss:F2}"); // Log activation
                    return;                                       // Exit early to allow breakeven to take effect
                }
                
                // Alternative: Immediate trailing (uncomment the lines below and comment out breakeven section above)
                // trailingActivated = true;  // Start trailing immediately
                
                // Progressive tightening - reduce trailing distance as profit increases
                double baseDistance = TrailingStopPoints;         // Start with base trailing stop distance
                if (currentProfitPoints > ProfitTriggerPoints)   // Only tighten after breakeven trigger
                {
                    // Calculate how many profit levels above trigger point we are
                    double profitLevels = (currentProfitPoints - ProfitTriggerPoints) / 5; // Every 5 points = 1 level (faster acceleration)
                    // Calculate reduction amount based on profit levels and tightening rate
                    double reduction = profitLevels * ProgressiveTighteningRate * baseDistance;
                    // Apply reduction but maintain minimum distance (20% of original for more aggressive tightening)
                    baseDistance = Math.Max(baseDistance - reduction, baseDistance * 0.2);
                }
                
                // ATR-based dynamic distance calculation for volatility adaptation
                double atrValue = atr[0];                         // Get current ATR value
                double atrDistance = AtrMultiplier * atrValue / TickSize; // Convert ATR to points using multiplier
                double trailingDistance = Math.Max(baseDistance, atrDistance); // Use larger of progressive or ATR distance
                
                // Calculate new stop level based on current price and trailing distance
                double newStopLevel = Close[0] + (trailingDistance * TickSize);
                
                // Update stop only if new level is lower (more favorable) or if trailing not yet activated
                if (newStopLevel < shortStopLoss || !trailingActivated)
                {
                    shortStopLoss = newStopLevel;                 // Update stop loss level
                    trailingActivated = true;                     // Mark trailing as active
                    DrawStopLossLine(shortStopLoss);             // Draw updated stop line
                }
                
                // Check if stop loss has been hit and exit position
                if (High[0] >= shortStopLoss)
                {
                    ExitShort("Short Stop", "Short Entry");      // Exit short position
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"SHORT STOP HIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                        // Remove stop line from chart
                }
            }
            catch (Exception ex) // Handle any errors
            {
                Print($"Error in ProcessShortTrailingStop: {ex.Message}"); // Log error
            }
        }
        
        private void ProcessExistingPositions()
        {
            try
            {
                // Process trailing stops and exits for existing positions even outside trading hours
                
                // Handle trailing stop for Long position
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ProcessLongTrailingStop();
                }
                
                // Handle trailing stop for Short position
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ProcessShortTrailingStop();
                }
                
                // Process exit signals even outside trading hours
                bool bullishCrossover = CrossAbove(FastEMA, SlowEMA, 1);
                bool bearishCrossover = CrossBelow(FastEMA, SlowEMA, 1);
                
                // Exit on opposite crossover - check BOTH tracking systems
                if ((ourPositionState == MarketPosition.Long || Position.MarketPosition == MarketPosition.Long) && bearishCrossover)
                {
                    Print($"EXISTING POSITION EXIT: Bearish crossover - Our={ourPositionState}, NT={Position.MarketPosition}");
                    ExitLong("Long Exit Signal", "Long Entry");
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = DateTime.Now;
                    
                    RemoveStopLossLine();
                }
                
                if ((ourPositionState == MarketPosition.Short || Position.MarketPosition == MarketPosition.Short) && bullishCrossover)
                {
                    Print($"EXISTING POSITION EXIT: Bullish crossover - Our={ourPositionState}, NT={Position.MarketPosition}");
                    ExitShort("Short Exit Signal", "Short Entry");
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = DateTime.Now;
                    
                    RemoveStopLossLine();
                }
            }
            catch (Exception ex)
            {
                Print($"Error in ProcessExistingPositions: {ex.Message}");
            }
        }
        
        private void UpdateTimeFilterDisplay(string statusText, bool tradingAllowed)
        {
            try
            {
                Brush statusColor = tradingAllowed ? Brushes.LimeGreen : Brushes.Orange;
                
                // Remove old time filter text and draw new one
                RemoveDrawObject(timeFilterTextName);
                Draw.TextFixed(this, timeFilterTextName, "\n\n\n\n      " + statusText, TextPosition.TopLeft, 
                    statusColor, new SimpleFont("Arial", 11), 
                    Brushes.Transparent, Brushes.Transparent, 0);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateTimeFilterDisplay: {ex.Message}");
            }
        }
        
        private void UpdateTradingDirectionDisplay()
        {
            try
            {
                string directionText = "";
                Brush directionColor = Brushes.Cyan;
                
                switch (Direction)
                {
                    case TradingDirection.Both:
                        directionText = "TRADING DIRECTION: LONG & SHORT ENABLED";
                        directionColor = Brushes.Cyan;
                        break;
                    case TradingDirection.LongOnly:
                        directionText = "TRADING DIRECTION: LONG ONLY";
                        directionColor = Brushes.LimeGreen;
                        break;
                    case TradingDirection.ShortOnly:
                        directionText = "TRADING DIRECTION: SHORT ONLY";
                        directionColor = Brushes.Orange;
                        break;
                }
                
                // Remove old direction text and draw new one
                RemoveDrawObject(directionTextName);
                Draw.TextFixed(this, directionTextName, "\n\n\n\n\n      " + directionText, TextPosition.TopLeft, 
                    directionColor, new SimpleFont("Arial", 11), 
                    Brushes.Transparent, Brushes.Transparent, 0);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateTradingDirectionDisplay: {ex.Message}");
            }
        }
        
        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
        {
            // Sync our position tracking with NinjaTrader's position (for cases where orders are cancelled, etc.)
            Print($"OnPositionUpdate: NT Position = {marketPosition}, Quantity = {quantity}");
            Print($"OnPositionUpdate: Our Position BEFORE = {ourPositionState}, Our Quantity = {ourPositionQuantity}");
            
            // Check if our tracking is already correct - don't override if we just updated it
            TimeSpan timeSinceLastUpdate = DateTime.Now - lastPositionUpdate;
            bool recentlyUpdated = timeSinceLastUpdate.TotalSeconds < 2; // Within 2 seconds
            
            if (recentlyUpdated && ourPositionState == marketPosition && ourPositionQuantity == quantity)
            {
                Print($"OnPositionUpdate: Skipping update - our tracking is already correct and recently updated");
                return;
            }
            
            // Update our tracking to match actual position only if needed
            ourPositionState = marketPosition;
            ourPositionQuantity = quantity;
            lastPositionUpdate = DateTime.Now;
            
            Print($"OnPositionUpdate: Our Position AFTER = {ourPositionState}, Our Quantity = {ourPositionQuantity}");
            
            if (marketPosition == MarketPosition.Flat)
            {
                RemoveStopLossLine();
                trailingActivated = false;
                breakevenActivated = false;
                
                Print($"Position now FLAT - Our tracking synchronized");
                
                // Check if a new trade has been completed
                CheckForCompletedTrade();
            }
            
            // Update chart display when position changes
            UpdateChartDisplay();
        }
        
        private void CheckForCompletedTrade()
        {
            try
            {
                // Use a simpler approach to track completed trades
                // Don't access SystemPerformance directly during certain states
                if (State != State.Active && State != State.Realtime)
                    return;
                    
                // Just mark that we need to check trades later
                // We'll handle this in OnExecutionUpdate instead
            }
            catch (Exception ex)
            {
                Print($"Error in CheckForCompletedTrade: {ex.Message}");
            }
        }
        
        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity, Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            try
            {
                if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
                {
                    if (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort)
                    {
                        // Entry order
                        if (execution.Order.Name.Contains("Entry"))
                        {
                            entryPrice = price;
                            isLongPosition = execution.Order.OrderAction == OrderAction.Buy;
                            Print($"Trade opened: {execution.Order.Name} at {price:F2}");
                            
                            // Play nice "ting" sound for trade entry
                            Alert("TradeEntry", Priority.Medium, $"Trade Opened: {execution.Order.Name} at {price:F2}", 
                                  @"Alert1.wav", 10, Brushes.LimeGreen, Brushes.Black);
                        }
                    }
                    else if (execution.Order.OrderAction == OrderAction.Sell || execution.Order.OrderAction == OrderAction.BuyToCover)
                    {
                        // Exit order
                        if (execution.Order.Name.Contains("Stop") || execution.Order.Name.Contains("Exit"))
                        {
                            Print($"Trade closed: {execution.Order.Name} at {price:F2}");
                            
                            // Play nice "ting" sound for trade exit
                            Alert("TradeExit", Priority.Medium, $"Trade Closed: {execution.Order.Name} at {price:F2}", 
                                  @"Alert1.wav", 10, Brushes.Orange, Brushes.Black);
                            
                            // Calculate PnL manually
                            if (entryPrice > 0)
                            {
                                double tradePnL = 0;
                                double pointValue = Instrument.MasterInstrument.PointValue;
                                
                                if (isLongPosition)
                                {
                                    tradePnL = (price - entryPrice) * quantity * pointValue;
                                }
                                else
                                {
                                    tradePnL = (entryPrice - price) * quantity * pointValue;
                                }
                                
                                UpdateTradeStatistics(tradePnL);
                                entryPrice = 0; // Reset for next trade
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnExecutionUpdate: {ex.Message}");
            }
        }
        
        private void UpdateTradeStatistics(double tradePnL)
        {
            totalTrades++;
            tradePnLs.Add(tradePnL);
            totalPnL += tradePnL;
            
            // Track long vs short statistics
            if (isLongPosition)
            {
                longTrades++;
                longPnL += tradePnL;
                if (tradePnL > 0)
                {
                    longWins++;
                    if (tradePnL > largestWinLong)
                        largestWinLong = tradePnL;
                }
                else if (tradePnL < 0)
                {
                    longLosses++;
                    if (tradePnL < largestLossLong)
                        largestLossLong = tradePnL;
                }
            }
            else
            {
                shortTrades++;
                shortPnL += tradePnL;
                if (tradePnL > 0)
                {
                    shortWins++;
                    if (tradePnL > largestWinShort)
                        largestWinShort = tradePnL;
                }
                else if (tradePnL < 0)
                {
                    shortLosses++;
                    if (tradePnL < largestLossShort)
                        largestLossShort = tradePnL;
                }
            }
            
            // Overall win/loss tracking
            if (tradePnL > 0)
            {
                winningTrades++;
                grossProfit += tradePnL;
                if (tradePnL > largestWin)
                    largestWin = tradePnL;
                
                // Consecutive wins
                consecutiveWins++;
                consecutiveLosses = 0;
                if (consecutiveWins > maxConsecutiveWins)
                    maxConsecutiveWins = consecutiveWins;
            }
            else if (tradePnL < 0)
            {
                losingTrades++;
                grossLoss += Math.Abs(tradePnL);
                if (tradePnL < largestLoss)
                    largestLoss = tradePnL;
                
                // Consecutive losses
                consecutiveLosses++;
                consecutiveWins = 0;
                if (consecutiveLosses > maxConsecutiveLosses)
                    maxConsecutiveLosses = consecutiveLosses;
            }
            
            // Calculate drawdown
            if (totalPnL > peakPnL)
            {
                peakPnL = totalPnL;
                currentDrawdown = 0;
            }
            else
            {
                currentDrawdown = peakPnL - totalPnL;
                if (currentDrawdown > maxDrawdown)
                    maxDrawdown = currentDrawdown;
            }
            
            // Print dashboard periodically
            PrintDashboard();
            
            // Update chart display after each trade
            UpdateChartDisplay();
        }
        
        private void DrawStopLossLine(double price)
        {
            RemoveDrawObject(stopLossLineName);
            Draw.HorizontalLine(this, stopLossLineName, price, StopLossColor, DashStyleHelper.Solid, LineThickness);
        }
        
        private void RemoveStopLossLine()
        {
            RemoveDrawObject(stopLossLineName);
        }
        
        private void UpdateChartDisplay()
        {
            try
            {
                // Update detailed dashboard display (now includes all info)
                UpdateDashboardDisplay();
                
                // Update P&L history dashboard
                UpdatePnLHistoryDisplay();
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateChartDisplay: {ex.Message}");
            }
        }
        
        private void UpdateDashboardDisplay()
        {
            try
            {
                // Always show dashboard with status info, even with 0 trades
                
                // Get current position status with detailed information
                string positionStatus = "";
                positionStatus = $"NT: {Position.MarketPosition} (Qty: {Position.Quantity}) | OUR: {ourPositionState} (Qty: {ourPositionQuantity})";
                
                // Get current P&L
                string currentPnL = "";
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    try
                    {
                        double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                        currentPnL = $"{unrealizedPnL:C2}";
                    }
                    catch
                    {
                        if (entryPrice > 0)
                        {
                            double pnl = 0;
                            double pointValue = Instrument.MasterInstrument.PointValue;
                            
                            if (Position.MarketPosition == MarketPosition.Long)
                                pnl = (Close[0] - entryPrice) * Position.Quantity * pointValue;
                            else if (Position.MarketPosition == MarketPosition.Short)
                                pnl = (entryPrice - Close[0]) * Position.Quantity * pointValue;
                            
                            currentPnL = $"{pnl:C2}";
                        }
                        else
                        {
                            currentPnL = "N/A";
                        }
                    }
                }
                else
                {
                    currentPnL = $"{totalPnL:C2}";
                }
                
                // Get time filter status
                string timeStatus = "";
                if (UseTimeFilter)
                {
                    TimeSpan currentTime = Time[0].TimeOfDay.Add(TimeSpan.FromHours(-6));
                    if (currentTime < TimeSpan.Zero)
                        currentTime = currentTime.Add(TimeSpan.FromHours(24));
                    
                    TimeSpan startTime = StartTime.TimeOfDay;
                    TimeSpan endTime = EndTime.TimeOfDay;
                    
                    if (currentTime < startTime || currentTime > endTime)
                        timeStatus = $"CLOSED ({startTime:hh\\:mm}-{endTime:hh\\:mm}) | NY: {currentTime:hh\\:mm}";
                    else
                        timeStatus = $"OPEN ({startTime:hh\\:mm}-{endTime:hh\\:mm}) | NY: {currentTime:hh\\:mm}";
                }
                else
                {
                    timeStatus = "24/7 Trading";
                }
                
                // Get trading direction
                string directionStatus = "";
                switch (Direction)
                {
                    case TradingDirection.Both:
                        directionStatus = "LONG & SHORT";
                        break;
                    case TradingDirection.LongOnly:
                        directionStatus = "LONG ONLY";
                        break;
                    case TradingDirection.ShortOnly:
                        directionStatus = "SHORT ONLY";
                        break;
                }
                
                // Build comprehensive dashboard text
                string dashboardText = "";
                dashboardText += $"\n=== STRATEGY STATUS ===";
                dashboardText += $"\nPosition: {positionStatus}";
                dashboardText += $"\nCurrent P&L: {currentPnL}";
                dashboardText += $"\nTrading Hours: {timeStatus}";
                dashboardText += $"\nDirection: {directionStatus}";
                
                // Add EMA crossover status
                if (CurrentBar >= Math.Max(EmaPeriod1, EmaPeriod2))
                {
                    dashboardText += $"\n";
                    dashboardText += $"\n=== EMA STATUS ===";
                    dashboardText += $"\nFast EMA({EmaPeriod1}): {FastEMA[0]:F2}";
                    dashboardText += $"\nSlow EMA({EmaPeriod2}): {SlowEMA[0]:F2}";
                    
                    string emaRelation = FastEMA[0] > SlowEMA[0] ? "ABOVE" : "BELOW";
                    dashboardText += $"\nFast is {emaRelation} Slow";
                    
                    // Check for recent crossovers
                    bool bullishCross = CrossAbove(FastEMA, SlowEMA, 1);
                    bool bearishCross = CrossBelow(FastEMA, SlowEMA, 1);
                    
                    if (bullishCross)
                        dashboardText += $"\nSIGNAL: Bullish Cross NOW!";
                    else if (bearishCross)
                        dashboardText += $"\nSIGNAL: Bearish Cross NOW!";
                    else
                        dashboardText += $"\nNo crossover signal";
                }
                
                // Add trading statistics if we have trades
                if (totalTrades > 0)
                {
                    // Calculate win rates
                    double overallWinRate = totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0;
                    double longWinRate = longTrades > 0 ? (double)longWins / longTrades * 100 : 0;
                    double shortWinRate = shortTrades > 0 ? (double)shortWins / shortTrades * 100 : 0;
                    double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;
                    double avgWin = winningTrades > 0 ? grossProfit / winningTrades : 0;
                    double avgLoss = losingTrades > 0 ? grossLoss / losingTrades : 0;
                    
                    dashboardText += $"\n";
                    dashboardText += $"\n=== TRADING SUMMARY ===";
                    dashboardText += $"\nTotal Trades: {totalTrades}";
                    dashboardText += $"\nWin Rate: {overallWinRate:F1}% ({winningTrades}W/{losingTrades}L)";
                    dashboardText += $"\nSession P&L: {totalPnL:C2}";
                    dashboardText += $"\nProfit Factor: {profitFactor:F2}";
                    dashboardText += $"\n";
                    dashboardText += $"\n=== LONG TRADES ===";
                    dashboardText += $"\nLong: {longTrades} trades";
                    dashboardText += $"\nLong Win Rate: {longWinRate:F1}% ({longWins}W/{longLosses}L)";
                    dashboardText += $"\nLong P&L: {longPnL:C2}";
                    if (largestWinLong > 0) dashboardText += $"\nBest Long: {largestWinLong:C2}";
                    if (largestLossLong < 0) dashboardText += $"\nWorst Long: {largestLossLong:C2}";
                    dashboardText += $"\n";
                    dashboardText += $"\n=== SHORT TRADES ===";
                    dashboardText += $"\nShort: {shortTrades} trades";
                    dashboardText += $"\nShort Win Rate: {shortWinRate:F1}% ({shortWins}W/{shortLosses}L)";
                    dashboardText += $"\nShort P&L: {shortPnL:C2}";
                    if (largestWinShort > 0) dashboardText += $"\nBest Short: {largestWinShort:C2}";
                    if (largestLossShort < 0) dashboardText += $"\nWorst Short: {largestLossShort:C2}";
                    dashboardText += $"\n";
                    dashboardText += $"\n=== PERFORMANCE ===";
                    if (avgWin > 0) dashboardText += $"\nAvg Win: {avgWin:C2}";
                    if (avgLoss > 0) dashboardText += $"\nAvg Loss: {avgLoss:C2}";
                    dashboardText += $"\nMax Drawdown: {maxDrawdown:C2}";
                    dashboardText += $"\nMax Consec Wins: {maxConsecutiveWins}";
                    dashboardText += $"\nMax Consec Losses: {maxConsecutiveLosses}";
                    dashboardText += $"\nCurrent Streak: ";
                    if (consecutiveWins > 0) dashboardText += $"{consecutiveWins} wins";
                    else if (consecutiveLosses > 0) dashboardText += $"{consecutiveLosses} losses";
                    else dashboardText += "0";
                }
                
                // Remove old dashboard and draw new one
                RemoveDrawObject(dashboardTextName);
                Draw.TextFixed(this, dashboardTextName, dashboardText, TextPosition.BottomLeft, 
                    Brushes.White, new SimpleFont("Consolas", 12), 
                    new SolidColorBrush(Color.FromArgb(192, 0, 0, 255)), new SolidColorBrush(Color.FromArgb(192, 0, 0, 255)), 100);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateDashboardDisplay: {ex.Message}");
            }
        }
        
        private void UpdatePnLHistoryDisplay()
        {
            try
            {
                // Build P&L history dashboard text for last 10 trades
                string pnlHistoryText = "";
                pnlHistoryText += $"\n=== LAST 10 TRADES P&L ===";
                
                if (tradePnLs.Count > 0)
                {
                    // Get the last 10 trades (or fewer if we don't have 10 yet)
                    int startIndex = Math.Max(0, tradePnLs.Count - 10);
                    int tradesShown = tradePnLs.Count - startIndex;
                    
                    pnlHistoryText += $"\nShowing last {tradesShown} trade(s):";
                    pnlHistoryText += $"\n";
                    
                    // Display each trade with trade number and P&L
                    for (int i = startIndex; i < tradePnLs.Count; i++)
                    {
                        int tradeNumber = i + 1;
                        double tradePnL = tradePnLs[i];
                        
                        // Format P&L with color indication
                        string pnlStatus = tradePnL >= 0 ? "WIN" : "LOSS";
                        pnlHistoryText += $"\nTrade #{tradeNumber}: {tradePnL:C2} ({pnlStatus})";
                    }
                    
                    // Add summary for displayed trades
                    var displayedTrades = tradePnLs.Skip(startIndex).ToList();
                    double displayedTotal = displayedTrades.Sum();
                    int displayedWins = displayedTrades.Count(x => x > 0);
                    int displayedLosses = displayedTrades.Count(x => x < 0);
                    double displayedWinRate = displayedTrades.Count > 0 ? (double)displayedWins / displayedTrades.Count * 100 : 0;
                    
                    pnlHistoryText += $"\n";
                    pnlHistoryText += $"\n=== LAST {tradesShown} SUMMARY ===";
                    pnlHistoryText += $"\nTotal P&L: {displayedTotal:C2}";
                    pnlHistoryText += $"\nWin Rate: {displayedWinRate:F1}%";
                    pnlHistoryText += $"\nWins: {displayedWins} | Losses: {displayedLosses}";
                    
                    if (displayedTrades.Count > 0)
                    {
                        double maxWin = displayedTrades.Where(x => x > 0).DefaultIfEmpty(0).Max();
                        double maxLoss = displayedTrades.Where(x => x < 0).DefaultIfEmpty(0).Min();
                        
                        if (maxWin > 0) pnlHistoryText += $"\nBest: {maxWin:C2}";
                        if (maxLoss < 0) pnlHistoryText += $"\nWorst: {maxLoss:C2}";
                    }
                }
                else
                {
                    pnlHistoryText += $"\nNo trades completed yet";
                    pnlHistoryText += $"\n";
                    pnlHistoryText += $"\nWaiting for first trade...";
                }
                
                // Remove old P&L history dashboard and draw new one at bottom right
                RemoveDrawObject(pnlHistoryTextName);
                Draw.TextFixed(this, pnlHistoryTextName, pnlHistoryText, TextPosition.BottomRight, 
                    Brushes.White, new SimpleFont("Consolas", 12), 
                    new SolidColorBrush(Color.FromArgb(192, 0, 100, 0)), new SolidColorBrush(Color.FromArgb(192, 0, 100, 0)), 100);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdatePnLHistoryDisplay: {ex.Message}");
            }
        }
        
        // Simple dashboard using Print statements instead of OnRender
        private void PrintDashboard()
        {
            try
            {
                if (totalTrades % 5 == 0 && totalTrades > 0) // Print every 5 trades
                {
                    Print("=== DASHBOARD TRADING ===");
                    Print($"Total trades: {totalTrades}");
                    Print($"Winners: {winningTrades}");
                    Print($"Losers: {losingTrades}");
                    Print($"Win Rate: {(totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0):F1}%");
                    Print($"Total PnL: {totalPnL:C2}");
                    Print($"Profit Factor: {(grossLoss > 0 ? grossProfit / grossLoss : 0):F2}");
                    Print("========================");
                }
            }
            catch (Exception ex)
            {
                Print($"Error in PrintDashboard: {ex.Message}");
            }
        }

        #region Properties
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period 1 (Fast)", Description = "Period for the first EMA (fast)", Order = 1, GroupName = "Parameters")]
        public int EmaPeriod1 { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period 2 (Slow)", Description = "Period for the second EMA (slow)", Order = 2, GroupName = "Parameters")]
        public int EmaPeriod2 { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trailing Stop Points", Description = "Base points for trailing stop (will be adjusted by ATR and profit)", Order = 3, GroupName = "Parameters")]
        public int TrailingStopPoints { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "ATR Multiplier", Description = "Multiplier for ATR-based trailing distance", Order = 4, GroupName = "Parameters")]
        public double AtrMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Profit Trigger Points", Description = "Points of profit needed to activate breakeven protection", Order = 5, GroupName = "Parameters")]
        public int ProfitTriggerPoints { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.05, 0.5)]
        [Display(Name = "Progressive Tightening Rate", Description = "Rate at which trailing stop tightens as profit increases (0.1 = 10% tighter per profit level)", Order = 6, GroupName = "Parameters")]
        public double ProgressiveTighteningRate { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Description = "Number of contracts", Order = 7, GroupName = "Parameters")]
        public int Quantity { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Trading Direction", Description = "Select trading direction: Both, Long Only, or Short Only", Order = 8, GroupName = "Parameters")]
        public TradingDirection Direction { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Time Filter", Description = "Enable/disable time filter", Order = 9, GroupName = "Parameters")]
        public bool UseTimeFilter { get; set; }
        
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time (NY)", Description = "Trade start time (New York time)", Order = 10, GroupName = "Parameters")]
        public DateTime StartTime { get; set; }
        
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time (NY)", Description = "Trade end time (New York time)", Order = 11, GroupName = "Parameters")]
        public DateTime EndTime { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Stop Loss Color", Description = "Color of the stop loss line", Order = 12, GroupName = "Display")]
        public Brush StopLossColor { get; set; }
        
        [Browsable(false)]
        public string StopLossColorSerializable
        {
            get { return Serialize.BrushToString(StopLossColor); }
            set { StopLossColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Line Thickness", Description = "Thickness of the stop loss line", Order = 13, GroupName = "Display")]
        public int LineThickness { get; set; }
        
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
// This code will be generated automatically by NinjaTrader during compilation
#endregion
