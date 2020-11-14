using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class NewBot : Robot
    {
        #region 50Pips User Input Parameters

        /// <summary>
        /// The lot size to open a position with
        /// </summary>
        [Parameter("Lot size per order", DefaultValue = 1)]
        public double LotSize { get; set; }

        /// <summary>
        /// Number of pips to count before opening a position in any direction
        /// </summary>
        [Parameter("Open At (Pips)", DefaultValue = 50, MinValue = 0)]
        public int Space { get; set; }

        /// <summary>
        /// The Amount of pips to use for stoploss
        /// </summary>
        [Parameter("Stop Loss (Pips)", DefaultValue = 50, MinValue = 0)]
        public int StopLoss { get; set; }

        /// <summary>
        /// The amount of pips to trail market price by, when in profit 
        /// </summary>
        [Parameter("Trail Price By (Pips)", DefaultValue = 50, MinValue = 0)]
        public int TrailBy { get; set; }

        /// <summary>
        /// Close all positions on DeInitialization 
        /// </summary>
        [Parameter("Close all positions on Exit", DefaultValue = YesNoOptions.NO)]
        public YesNoOptions CloseAllPositionsOnDeInit { get; set; }

        #endregion

        #region Bot Related parameters

        /// <summary>
        /// Unique Bot-ID, Set using a GUID
        /// </summary>
        public string BotID;

        /// <summary>
        /// Bot Name requested by client
        /// </summary>
        public string BotName = "50Pips";

        /// <summary>
        /// The symbol on which 50Pips has been deployed to
        /// </summary>
        private string BotSymbol;

        /// <summary>
        /// The chart timeframe on which 50Pips has been deployed to
        /// </summary>
        private TimeFrame BotTimeFrame;

        /// <summary>
        /// Flag used to control when bot can open positions
        /// </summary>
        public bool CanOpenPosition = false;

        /// <summary>
        /// Current zero-line for counting pips before opening
        /// </summary>
        public double Zero_Line;

        /// <summary>
        /// Current buy-line calculated from zero-line
        /// </summary>
        public double Buy_Line;

        /// <summary>
        /// Current sell-line calculated from zero-line
        /// </summary>
        public double Sell_Line;

        /// <summary>
        /// Counter used to initialized trading, operations,
        /// Bot starts trading when counter increases to zero
        /// </summary>
        public int Initialization_Count = -1;

        /// <summary>
        /// END of Demo
        /// REMOVE FOR PRODUCTION
        /// </summary>
        private DateTime DemoEndTime = new DateTime(year: 2020, month: 10, day: 15, hour: 0, minute: 0, second: 0);
        #endregion

        #region 50Pips Robot Methods

        /// <summary>
        /// 50Pips Initialization Logic
        /// </summary>
        protected override void OnStart()
        {
            // REMOVE FOR PRODUCTION
            // SET DEMO TIME LIMIT
            var currentTime = DateTime.Now;

            if (currentTime >= DemoEndTime)
            {
                this.LogError(errorMessage: "Demo Expired at " + DemoEndTime.ToString());
                this.Stop();
            }

            // Set Bot Unique ID
            this.BotID = Guid.NewGuid().ToString();
            this.LogInformation(message: "Unique Id Set to " + this.BotID);

            // Detect Symbol 50Pips is been Executed on
            BotSymbol = Symbol.Name;
            this.LogInformation(message: this.BotSymbol + " detected as Bot symbol");

            // Detect Time Frame bot has been set on
            this.BotTimeFrame = this.Chart.TimeFrame;
            this.LogInformation(message: this.BotTimeFrame.ToString() + " detected as Bot Time Frame");

            // Allow bot to start opening positions
            CanOpenPosition = true;

            // Set position opened event handler
            this.Positions.Opened += Positions_Opened;

            // Set Position Closed event handler
            this.Positions.Closed += Positions_Closed;
        }

        /// <summary>
        /// This Method gets called on every tick
        /// </summary>
        protected override void OnTick()
        {
            // respect this.Initiailization_Count
            if (this.Initialization_Count < 0)
                return;

            this.OpenPosition();
            this.BreakEven();
            this.TrailStop();
        }

        /// <summary>
        /// This method gets called on the opening of every new bar on the chart
        /// </summary>
        protected override void OnBar()
        {
            // Initialize trading operations after first new bar
            if (this.Initialization_Count < 0)
                this.Initialization_Count++;

            // skip procedure if bot cannot open positions
            if (!this.CanOpenPosition)
                return;

            // update this.Zero_Line, this.Buy_line and this.SellLine
            this.Zero_Line = this.Bars.LastBar.Open;
            this.Buy_Line = this.Zero_Line + this.ConvertPipsToDecimal(this.Space);
            this.Sell_Line = this.Zero_Line - this.ConvertPipsToDecimal(this.Space);

            this.LogInformation(message: "New bar detected for " + this.BotName + " bot with id " + this.BotID);
            this.LogInformation(message: "Resetting zero_line to " + this.Zero_Line.ToString() + " Resetting buy_line to " + this.Buy_Line.ToString() + " Resetting sell_line to " + this.Sell_Line.ToString());

            // Remove all lines on chart
            this.ClearAllLines();

            // draw new zero_line, buy_line and sell_line
            this.DrawHorizontalLine(name: "zero_line", price: this.Zero_Line, color: Color.DarkGoldenrod);
            this.DrawHorizontalLine(name: "buy_line", price: this.Buy_Line, color: Color.DarkBlue);
            this.DrawHorizontalLine(name: "sell_line", price: this.Sell_Line, color: Color.DarkRed);
        }

        /// <summary>
        /// 50Pips DeInitialization Logic
        /// </summary>
        protected override void OnStop()
        {
            // clear all pending orders
            this.LogInformation("Stop requested for " + this.BotName + " bot with id " + this.BotID);
            this.ClearAllPendingOrders();

            // clear all open positions if set by user
            if (this.CloseAllPositionsOnDeInit == YesNoOptions.YES)
                this.CloseAllPositions();

            // remove all objects from chart
            this.ClearAllLines();

            this.LogInformation("DeInitialization complete for " + this.BotName + " bot with id " + this.BotID);
        }
        #endregion

        #region Event handlers

        /// <summary>
        /// Event Handler for positions closed event
        /// NOTE :: This event is called when a position is closed, whether that position
        /// was closed by this bot or another bot or by a user manually
        /// </summary>
        /// <param name="obj"></param>
        private void Positions_Closed(PositionClosedEventArgs obj)
        {
            // null check event argument
            if (obj == null)
                return;
            if (obj.Position == null)
                return;

            // check if event was meant for this bot, using the position label
            if (obj.Position.Label != this.BotID)
                return;

            // continue to handle event
            var closedPosition = obj.Position;
            var closeReason = obj.Reason;

            this.LogInformation(closedPosition.TradeType.ToString() + " Position closed for " + this.BotName + " bot with id " + this.BotID + ", Reason: " + closeReason.ToString());

            // re-activate bot CanSendOrders flag if no positions or orders are open
            if (this.CountPositions() == 0 && this.CountPedingOrders() == 0)
            {
                this.ClearAllLines();
                this.Zero_Line = 0;
                this.Buy_Line = 0;
                this.Sell_Line = 0;
                this.CanOpenPosition = true;
            }
        }

        /// <summary>
        /// Event Handler for positions opened event 
        /// NOTE :: This event is called when a position is closed, whether that position 
        /// was opened by this bot or another bot or by a user manually
        /// </summary>
        /// <param name="obj"></param>
        private void Positions_Opened(PositionOpenedEventArgs obj)
        {
            // null check event argument
            if (obj == null)
                return;
            if (obj.Position == null)
                return;

            var openedPosition = obj.Position;

            // check if the event raised was meant for this bot, using the position label
            if (openedPosition.Label != this.BotID)
                return;

            // remove buy_line and sell_line
            this.RemoveChartObject("buy_line");
            this.RemoveChartObject("sell_line");
        }
        #endregion
    }

    /// <summary>
    /// assembly-specific, 50Pips trade extensions
    /// </summary>
    static internal class TradeOperations
    {
        /// <summary>
        /// Open Positions
        /// </summary>
        /// <param name="bot"></param>
        public static void OpenPosition(this NewBot bot)
        {
            // verify bot.CanOpenPosition
            if (!bot.CanOpenPosition)
                return;

            // skip if zero_line is set to zero
            if (bot.Zero_Line == 0)
                return;

            // check price relation to zero_line
            var currentPriceRelationToZero_Line = bot.PriceRelationToZeroLine();

            // handle if price is above zero_line
            if (currentPriceRelationToZero_Line == "above")
            {
                // skip if Ask price is below bot.Buy_Line
                if (bot.Ask < bot.Buy_Line)
                    return;

                // open buy position if price has reached bot.Buy_Line
                var buyPositionResult = bot.ExecuteMarketOrder(tradeType: TradeType.Buy, symbolName: bot.SymbolName, volume: bot.ConvertLotsToVolume(bot.LotSize), label: bot.BotID);

                // skip if market order failed to be executed
                if (!buyPositionResult.IsSuccessful)
                {
                    bot.LogError("Failed to Open Buy Position for " + bot.BotName + " bot with id " + bot.BotID);
                    return;
                }

                // modify position stop loss
                buyPositionResult.Position.ModifyStopLossPips(bot.StopLoss);
                bot.LogInformation("Buy Position opened for " + bot.BotName + " bot with id " + bot.BotID);

                // stop bot from opening another position
                bot.CanOpenPosition = false;
            }

            else if (currentPriceRelationToZero_Line == "below")
            {
                // skip if Bid price is Above bot.Sell_Line
                if (bot.Bid > bot.Sell_Line)
                    return;

                // open sell position if price has reached bot.Sell_Line
                var sellPositionResult = bot.ExecuteMarketOrder(tradeType: TradeType.Sell, symbolName: bot.SymbolName, volume: bot.ConvertLotsToVolume(bot.LotSize), label: bot.BotID);

                // skip if market order failed to be executed
                if (!sellPositionResult.IsSuccessful)
                {
                    bot.LogError("Failed to Open sell Position for " + bot.BotName + " bot with id " + bot.BotID);
                    return;
                }

                // modify position stop loss
                sellPositionResult.Position.ModifyStopLossPips(bot.StopLoss);
                bot.LogInformation("Sell Position opened for " + bot.BotName + " bot with id " + bot.BotID);

                // stop bot from opening another position
                bot.CanOpenPosition = false;
            }
        }

        /// <summary>
        /// Price Trailing procedure for positions in profit
        /// </summary>
        /// <param name="bot"></param>
        public static void TrailStop(this NewBot bot)
        {
            // attempt trail-stop only if a position is open
            if (bot.CanOpenPosition)
                return;

            // retrieve all open positions for this bot
            var openPositions = bot.Positions.Where(p => p.Label == bot.BotID).ToList();

            foreach (var position in openPositions)
            {
                // handle buy positions trailing
                if (position.TradeType == TradeType.Buy)
                {
                    // check if position has broken even
                    if (position.StopLoss < position.EntryPrice)
                        continue;

                    // trail price by bot.TrailBy
                    if (bot.Bid - position.StopLoss >= bot.ConvertPipsToDecimal(bot.TrailBy))
                        if (bot.Bid - bot.ConvertPipsToDecimal(bot.TrailBy) > position.EntryPrice)
                            position.ModifyStopLossPrice(bot.Bid - bot.ConvertPipsToDecimal(bot.TrailBy));
                }

                else if (position.TradeType == TradeType.Sell)
                {
                    // check if position has broken even
                    if (position.StopLoss > position.EntryPrice)
                        continue;

                    // trail price by bot.TrailBy
                    if (position.StopLoss - bot.Ask >= bot.ConvertPipsToDecimal(bot.TrailBy))
                        if (bot.Ask + bot.ConvertPipsToDecimal(bot.TrailBy) < position.EntryPrice)
                            position.ModifyStopLossPrice(bot.Ask + bot.ConvertPipsToDecimal(bot.TrailBy));
                }

            }
        }

        /// <summary>
        /// Break even procedure for positions in profit
        /// </summary>
        /// <param name="bot"></param>
        public static void BreakEven(this NewBot bot)
        {
            // attempt break-event only if a position is open
            if (bot.CanOpenPosition)
                return;

            // retrieve all open positions for this bot
            var openPositions = bot.Positions.Where(p => p.Label == bot.BotID);

            foreach (var position in openPositions)
            {
                // check if position is in profit
                if (position.NetProfit <= 0)
                    continue;

                // handle buy positions
                if (position.TradeType == TradeType.Buy)
                {
                    // check if position has already broken even
                    if (position.StopLoss >= position.EntryPrice)
                        continue;

                    // break-even if price and entry-price difference is >= bot.TrailBy
                    if (bot.Bid - position.EntryPrice >= bot.ConvertPipsToDecimal(bot.TrailBy))
                        position.ModifyStopLossPrice(position.EntryPrice + bot.ConvertPipsToDecimal(2));
                }

                // handle sell positions
                if (position.TradeType == TradeType.Sell)
                {
                    // check if position has already broken even
                    if (position.StopLoss <= position.EntryPrice)
                        continue;

                    // break-even if price and entry-price difference is >= bot.TrailBy
                    if (position.EntryPrice - bot.Ask >= bot.ConvertPipsToDecimal(bot.TrailBy))
                        position.ModifyStopLossPrice(position.EntryPrice - bot.ConvertPipsToDecimal(2));
                }
            }


        }

        /// <summary>
        /// Clear all Pending Orders opened by this bot
        /// </summary>
        /// <param name="bot"></param>
        public static void ClearAllPendingOrders(this NewBot bot)
        {
            bot.LogInformation("Clearing all pending orders for " + bot.BotName + " bot with id " + bot.BotID);
            foreach (var order in bot.PendingOrders.Where(p => p.Label == bot.BotID))
                order.Cancel();
        }

        /// <summary>
        /// Close all Open Positions for this bot
        /// </summary>
        /// <param name="bot"></param>
        public static void CloseAllPositions(this NewBot bot)
        {
            bot.LogInformation("Closing all Positions for " + bot.BotName + " bot with id " + bot.BotID);
            foreach (var order in bot.Positions.Where(p => p.Label == bot.BotID))
                order.Close();
        }
    }

    /// <summary>
    /// Assembly specific, static class for logging
    /// </summary>
    static internal class Logger
    {
        /// <summary>
        /// Log messages tagged with information
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="message"></param>
        public static void LogInformation(this NewBot bot, string message)
        {
            var timeStamp = DateTime.Now.ToString();
            bot.Print(bot.BotName + "[info] :: " + message);
        }

        /// <summary>
        /// Log messages tagged with error
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="errorMessage"></param>
        public static void LogError(this NewBot bot, string errorMessage)
        {
            var timeStamp = DateTime.Now.ToString();
            bot.Print(bot.BotName + "[error] :: " + errorMessage);
        }
    }

    /// <summary>
    /// Assembly-Specific, static helper methods
    /// </summary>
    static internal class HelperMethods
    {
        /// <summary>
        /// Draw a Non-Interactive Horizontal Line on the chart
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="name"></param>
        /// <param name="price"></param>
        /// <param name="color"></param>
        public static void DrawHorizontalLine(this NewBot bot, string name, double price, Color color)
        {
            var line = bot.Chart.DrawHorizontalLine(name: name, y: price, color: color, thickness: 1, lineStyle: LineStyle.Solid);
            line.IsInteractive = false;
        }

        /// <summary>
        /// Remove all objects from the chart
        /// </summary>
        /// <param name="bot"></param>
        public static void ClearAllLines(this NewBot bot)
        {
            bot.Chart.RemoveAllObjects();
        }

        /// <summary>
        /// Remove a Specific ChartObject
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="name"></param>
        public static void RemoveChartObject(this NewBot bot, string name)
        {
            bot.Chart.RemoveObject(objectName: name);
        }

        /// <summary>
        /// Convert Lots to volume units
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="lots"></param>
        /// <returns></returns>
        public static double ConvertLotsToVolume(this NewBot bot, double lots)
        {
            return bot.Symbol.QuantityToVolumeInUnits(lots);
        }

        /// <summary>
        /// Convert volume units to lots
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="volume"></param>
        /// <returns></returns>
        public static double ConvertVolumeToLots(this NewBot bot, double volume)
        {
            return bot.Symbol.VolumeInUnitsToQuantity(volume);
        }

        /// <summary>
        /// Convert pips in integer form to decimal form for price calculations
        /// </summary>
        /// <param name="bot"></param>
        /// <param name="pips"></param>
        /// <returns></returns>
        public static double ConvertPipsToDecimal(this NewBot bot, int pips)
        {
            return bot.Symbol.PipSize * pips;
        }

        /// <summary>
        /// Return the number of open positions for this bot
        /// </summary>
        /// <param name="bot"></param>
        /// <returns></returns>
        public static int CountPositions(this NewBot bot)
        {
            return bot.Positions.Count;
        }

        /// <summary>
        /// return the number of open pending orders for this bot
        /// </summary>
        /// <param name="bot"></param>
        /// <returns></returns>
        public static int CountPedingOrders(this NewBot bot)
        {
            return bot.PendingOrders.Count;
        }

        /// <summary>
        /// returns a string indicating if both the Ask and Bid price levels are on any one side of this.Zero_Line
        /// </summary>
        /// <param name="bot"></param>
        /// <returns></returns>
        public static string PriceRelationToZeroLine(this NewBot bot)
        {
            // return above if both ask and bid prices are above the zero_line
            if (bot.Ask > bot.Zero_Line && bot.Bid > bot.Zero_Line)
                return "above";

            // return below if both ask and bid prices are below the zero_line
            if (bot.Ask < bot.Zero_Line && bot.Bid < bot.Zero_Line)
                return "below";

            // return nothing otherwise
            return string.Empty;
        }

    }

    /// <summary>
    /// Yes, No enumeration for making quick choices
    /// </summary>
    public enum YesNoOptions
    {
        YES,
        NO
    }
}
