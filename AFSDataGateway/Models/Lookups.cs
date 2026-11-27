using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway.Models
{
    public class Lookups
    {
        public SelectList RiskLevels { get; set; }

    }

    public class CSVTradingData
    {
        public string CurrentWeek { get; set; }
        public string CustomerGroup { get; set; }
        public string Customer { get; set; }
        public string CarrierGroup { get; set; }
        public string Carrier { get; set; }
        public string SupplierRef { get; set; }
        public string ConsignmentNote { get; set; }
        public string JobNumber { get; set; }
        public string Cons_Date { get; set; }
    
        public string Moveit_DespatchDate { get; set; }
        public string Moveit_FreightStatus { get; set; }
        public string from { get; set; }
        public string to { get; set; }
        public string option_code { get; set; }
        public string work_code { get; set; }

        public string AncillaryDescription { get; set; }
        public string quantity { get; set; }
        public string space { get; set; }
        public string lift { get; set; }
        public string weight { get; set; }
        public string chg_kg { get; set; }
        public string pallet { get; set; }
        public string hours { get; set; }
        public string cubic { get; set; }
        public string cus_ref { get; set; }
        public string Moveit_LineReference { get; set; }
        public string Sell_Freight_Charges { get; set; }
        public string Sell_Other_Charges { get; set; }
        public string Sell_Fuel_Surch { get; set; }
        public string Sell_Total_Nett { get; set; }
        public string Buy_Freight_Charges { get; set; }
        public string Buy_Other_Charges { get; set; }
        public string Buy_Fuel_Surcharge { get; set; }
        public string Buy_Total_Nett { get; set; }
        public string Nett_Margin { get; set; }
        public string FSL___Sell { get; set; }
        public string C_FSL_Buy { get; set; }
        public string FSL___Var { get; set; }
        public string ExternalComments { get; set; }
        public string Moveit_TotWeightKg { get; set; }
        public string Moveit_TotVolM { get; set; }
        public string Moveit_RateIncSurch { get; set; }
        public string Moveit_LineOfBusiness { get; set; }
        public string Moveit_ActualDeliveryDate { get; set; }
    }

}