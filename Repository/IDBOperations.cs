using JobAppHR.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace JobAppHR.Repository
{
    public interface IDBOperations
    {
        DataTable SelectRows(string tableName, string fieldSet, string keyField, string keyValue, string whereClause, string keyFieldDataType = "", params SqlParameter[] parameters);
        DataTable SelectRows(string sql);
        DataTable SelectRows(string sql, params SqlParameter[] parameters);
        List<SelectListItem> AnyDataList(string tableName, string valueField, string textField, string whereClause, string sortOrder);
        string UpdateRecords(string tableName, DataTable tempTable, string keyField, string keyValue, string keyFieldDataType = "", string whereCondition = "");
        string UpdateRecords(string tableName, DataTable tempTable, string keyField);
        string UpdateRecords(string sql);
        string UpdateRecords(string sql, params SqlParameter[] parameters);
        string InsertRecords(string tableName, DataTable tempTable, bool isIdentity, string identityField = "");
        string GetJobPositionCodeById(int jobPositionID);
        string GetJobPositionName(string intakeCode = "", string jobPositionCode = "");
        DataTable GetFilteringCriteriaOfJobPosition(string intakeCode);
        bool IsTalentPoolEnabled();
        void UpdateTalentPoolStatus(bool isEnabled);
    }
}
