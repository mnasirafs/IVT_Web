

var app = angular.module("myApp", []);
app.controller("myCtrl", function ($scope, $http) {
    debugger;
    $scope.InsertData = function () {
        var Action = document.getElementById("btnSave").getAttribute("value");
        if (Action == "Submit") {
            $scope.Comment = {};
            $scope.Comment.CustomerGroup = $scope.CustomerGroup;
            $scope.Comment.RiskLevel = $scope.RiskLevel;
            $scope.Comment.CustomerComment = $scope.CustomerComment;
            $scope.Comment.RevenueComment = $scope.RevenueComment;
            $scope.Comment.ContractStatus = $scope.ContractStatus;
            $scope.Comment.Executive = $scope.Executive;
            $scope.Comment.ActionRequired = $scope.ActionRequired;
            $scope.Comment.DueDate = $scope.DueDate;
            $http({
                method: "post",
                url: "http://localhost:44379/CustomerAtRisk/Insert_AccountComment",
                datatype: "json",
                data: JSON.stringify($scope.Comment)
            }).then(function (response) {
                alert(response.data);
                $scope.GetAllData();
                $scope.CustomerGroup = "";
                $scope.CustomerComment = "";
                $scope.CustomerComment = "";
            })
        } else {
            $scope.Comment = {};
            $scope.Comment.CustomerGroup = $scope.CustomerGroup;
            $scope.Comment.RiskLevel = $scope.RiskLevel;
            $scope.Comment.CustomerComment = $scope.CustomerComment;
            $scope.Comment.RevenueComment = $scope.RevenueComment;
            $scope.Comment.ContractStatus = $scope.ContractStatus;
            $scope.Comment.Executive = $scope.Executive;
            $scope.Comment.ActionRequired = $scope.ActionRequired;
            $scope.Comment.DueDate = $scope.DueDate;
            $scope.Comment.Id = document.getElementById("Id_").value;
            $http({
                method: "post",
                url: "http://localhost:44379/CustomerAtRisk/Update_AccountComment",
                datatype: "json",
                data: JSON.stringify($scope.Comment)
            }).then(function (response) {
                alert(response.data);
                $scope.GetAllData();
                $scope.CustomerGroup = "";
                $scope.CustomerComment = "";
                $scope.CustomerComment = "";
                document.getElementById("btnSave").setAttribute("value", "Submit");
                document.getElementById("btnSave").style.backgroundColor = "cornflowerblue";
                document.getElementById("spn").innerHTML = "Add New CustomerAtRisk";
            })
        } 
    }
    $scope.GetAllData = function () {
        $http({
            method: "get",
            url: "http://localhost:44379/CustomerAtRisk/Get_AllAccountComments"
        }).then(function (response) {
            alert(response);
            $scope.CustomerAtRisks = response.data;
           
        }, function (er) {
            alert(er);
        })
    };
    $scope.DeleteComment = function (Comment) {
        $http({
            method: "post",
            url: "http://localhost:44379/CustomerAtRisk/Delete_AccountComment",
            datatype: "json",
            data: JSON.stringify(Comment)
        }).then(function (response) {
            alert(response.data);
            $scope.GetAllData();
        })
    };
    $scope.UpdateComment = function (Comment) {
        document.getElementById("Id_").value = Comment.Id;
        $scope.Comment.CustomerGroup = $scope.CustomerGroup;
        $scope.Comment.RiskLevel = $scope.RiskLevel;
        $scope.Comment.CustomerComment = $scope.CustomerComment;
        $scope.Comment.RevenueComment = $scope.RevenueComment;
        $scope.Comment.ContractStatus = $scope.ContractStatus;
        $scope.Comment.Executive = $scope.Executive;
        $scope.Comment.ActionRequired = $scope.ActionRequired;
        $scope.Comment.DueDate = $scope.DueDate;
        document.getElementById("btnSave").setAttribute("value", "Update");
        document.getElementById("btnSave").style.backgroundColor = "Yellow";
        document.getElementById("spn").innerHTML = "Update CustomerAtRisk Information";
    }
})  