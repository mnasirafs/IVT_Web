using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AFSDataGateway.Models
{
    public class DashExceptionSummary
    {

        public string RuleCategory { get; set; }

        public int? RuleCode { get; set; }
        public string ErrDesc { get; set; }

        public string ExceptionType { get; set; }
        public string ThresholdValue { get; set; }

        public int? CurrentWeekCount { get; set; }
        public int? LastWeekCount { get; set; }
        public int? Last13WeeksAvgCount { get; set; }
    }

    public class CustomerCountResult
    {
        public string FinanceGroup { get; set; }

        public string Carrier { get; set; }

        public string AccountManager { get; set; }
        public string LoginName { get; set; }
        public int? TotalCurrentWeekCount { get; set; }
        public int? TotalCurrentWeekTradingCount { get; set; }
        public double? AverageCurrentWeekTrading { get; set; }
        public int? TotalLastWeekCount { get; set; }
        public int? TotalLastWeekTradingCount { get; set; }
        public double? AverageLastWeekTrading { get; set; }
        public double? AverageLast13WeeksCount { get; set; }
        public int? TotalLast13WeeksTradingCount { get; set; }
        public double? AverageLast13WeeksTrading { get; set; }
    }


    public class AMCountResult
    {
        public string AccountManager { get; set; }
        public string LoginName { get; set; }
        public int? TotalCurrentWeekCount { get; set; }
        public int? TotalCurrentWeekTradingCount { get; set; }
        public double? AverageCurrentWeekTradingAVG { get; set; }
        public int? TotalLastWeekCount { get; set; }
        public int? TotalLastWeekTradingCount { get; set; }
        public double? AverageLast13WeeksAvgCount { get; set; }
        public int? TotalLast13WeeksTradingCount { get; set; }
        public double? AverageLast13WeeksTradingCountAVG { get; set; }
        public int? TotalExceptions { get; set; }
        public int? TotalCustomer { get; set; }
        public int? TotalCarrier { get; set; }
        public decimal? SellTotalNett { get; set; }
        public decimal? BuyTotalNett { get; set; }
        public decimal? TotalWeight { get; set; }
        public decimal? TotalCubic { get; set; }
        public decimal? TotalQuantity { get; set; }
        public int? HeldCount { get; set; }
        public int? TotalAllConnotes { get; set; }
        public decimal? ExceptionRate { get; set; }
        public int? TotalAncillaries { get; set; }
        public decimal? AncillariesRate { get; set; }
    }



    public partial class Custom_Dash_TotalMeasures_Admin
    {
        public Nullable<long> Id { get; set; }
        public Nullable<int> TotalExceptions { get; set; }
        public Nullable<decimal> ExceptionRate { get; set; }
        public int TotalCustomer { get; set; }
        public Nullable<int> TotalCarrier { get; set; }
        public decimal total_nett { get; set; }
        public decimal TotalWeight { get; set; }
        public decimal Cubic { get; set; }
        public decimal buy_total_nett { get; set; }
        public Nullable<decimal> TotalQuantity { get; set; }
        public int HeldCount { get; set; }
        public int TotalAllConnotes { get; set; }
        public int TotalAncillaries { get; set; }
        public int TotalManualEntry { get; set; }
        public int TotalNotInMoveit { get; set; }
        public Nullable<decimal> ManualEntryRate { get; set; }
        public Nullable<decimal> NotInMoveitRate { get; set; }
        public Nullable<decimal> AncillaryRate { get; set; }
        public Nullable<int> LastWeekCount { get; set; }
        public Nullable<int> LastWeekManualEntryCount { get; set; }
        public Nullable<int> LastWeekNotInMoveitCount { get; set; }
        public Nullable<int> LastWeekTradingCount { get; set; }
        public Nullable<decimal> LastWeekManualEntryRate { get; set; }
        public Nullable<decimal> LastWeekNotInMoveitRate { get; set; }
        public int LastWeekTotalAncillaries { get; set; }
        public Nullable<int> Last13WeeksCount { get; set; }
        public Nullable<int> Last13WeeksTradingCount { get; set; }
        public int Last13WeeksTotalAncillaries { get; set; }
        public Nullable<decimal> L13WAncillariesRate { get; set; }
        public Nullable<decimal> LWAncillariesRate { get; set; }

        public Nullable<decimal> L13WExceptionRate { get; set; }
        public Nullable<decimal> LWExceptionRate { get; set; }
        public Nullable<int> L13WManualEntryCount { get; set; }
        public Nullable<int> L13WNotInMoveitCount { get; set; }
        public Nullable<decimal> L13WManualEntryRate { get; set; }
        public Nullable<decimal> L13WNotInMoveitRate { get; set; }



    }

    public class DashboardViewModel
    {
        public List<Dash_CommonMeasure> CommonMeasureData { get; set; }
        public List<Dash_Top10Carrier> Top10CarrierData { get; set; }
        
        public List<Dash_Top10Customer> Top10CustomerData { get; set; }
        public List<Dash_Top10Exception> Top10ExceptionData { get; set; }
        public List<vDash_TotalMeasures> TotalMeasuresData { get; set; }

        public List<Custom_Dash_TotalMeasures_Admin> TotalMeasuresData_Admin { get; set; }
        public List<Dash_LastIVTRun> LastIVTRunData { get; set; }

        public List<DashExceptionSummary> Exception_summary_list { get; set; }

        public List<CustomerCountResult> Customer_Count_list { get; set; }

        public List<AMCountResult> AM_Count_list { get; set; }




        public string CurrentWeek { get; set; }
    }


}
