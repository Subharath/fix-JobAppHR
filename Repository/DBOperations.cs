using JobAppHR.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace JobAppHR.Repository
{
    public class DBOperations : IDBOperations
    {
         private readonly ILogger<DBOperations>? _logger;

        public DBOperations(ILogger<DBOperations>? logger = null)
        {
            _logger = logger;
        }
        
        public DataTable SelectRows(string tableName, string fieldSet, string keyField, string keyValue, string whereClause, string keyFieldDataType = "", params SqlParameter[] parameters)
        {
            DataTable dtblResult = new DataTable();
            using SqlConnection con = _dbConnection.GetDbConnection();

            using SqlCommand sqlCmd = con.CreateCommand();
            {
                con.Open();
                sqlCmd.Connection = con;

                if (string.IsNullOrEmpty(whereClause) && !string.IsNullOrEmpty(keyField) && !string.IsNullOrEmpty(keyValue))
                {
                    string paramName = "@" + keyField.Replace(".", "_").Replace(" ", "");
                    whereClause = "WHERE " + keyField + " = " + paramName;

                    if (!string.IsNullOrEmpty(keyFieldDataType) && (keyFieldDataType.Equals("int", StringComparison.OrdinalIgnoreCase) || keyFieldDataType.Equals("integer", StringComparison.OrdinalIgnoreCase)) && int.TryParse(keyValue, out int intVal))
                    {
                        sqlCmd.Parameters.Add(new SqlParameter(paramName, SqlDbType.Int) { Value = intVal });
                    }
                    else if (!string.IsNullOrEmpty(keyFieldDataType) && decimal.TryParse(keyValue, out decimal decVal))
                    {
                        sqlCmd.Parameters.Add(new SqlParameter(paramName, SqlDbType.Decimal) { Value = decVal });
                    }
                    else
                    {
                        sqlCmd.Parameters.Add(new SqlParameter(paramName, SqlDbType.VarChar) { Value = keyValue });
                    }
                }

                if (!string.IsNullOrEmpty(whereClause) && !whereClause.Trim().ToUpper().StartsWith("WHERE"))

                 if (parameters != null && parameters.Length > 0)
                {
                    sqlCmd.Parameters.AddRange(parameters);
                }

                sqlCmd.CommandText = "SELECT " + fieldSet + " FROM " + tableName + (string.IsNullOrEmpty(whereClause) ? "" : " " + whereClause);

                SqlDataAdapter sqlDa = new SqlDataAdapter(sqlCmd);

                //SqlDataAdapter sqlDa = new SqlDataAdapter("SELECT " + fieldSet + " FROM " + tableName + " " + whereClause, DbCon.myConnection);
                sqlDa.Fill(dtblResult);
            }

            return dtblResult;
        }

        public DataTable SelectRows(string sql)
        {
            return SelectRows(sql, Array.Empty<SqlParameter>());
        }

        public DataTable SelectRows(string sql, params SqlParameter[] parameters)
        {
            DataTable dtblResult = new DataTable();
            using SqlConnection con = _dbConnection.GetDbConnection();

            using SqlCommand sqlCmd = con.CreateCommand();
            {
                sqlCmd.CommandText = sql;

                if (parameters != null && parameters.Length > 0)
                {
                    sqlCmd.Parameters.AddRange(parameters);
                }

                SqlDataAdapter sqlDa = new SqlDataAdapter(sqlCmd);

                sqlDa.Fill(dtblResult);
            }

            return dtblResult;
        }

        public List<SelectListItem> AnyDataList(string tableName, string valueField, string textField, string whereClause, string sortOrder)
        {
            DataTable dtblData = new DataTable();
            List<SelectListItem> dataList = new List<SelectListItem>();

            string sql = "SELECT " + valueField;

            if (valueField != textField)
                sql = sql + "," + textField;

            sql = sql + " FROM " + tableName;

            if (!string.IsNullOrEmpty(whereClause))
                sql = sql + " WHERE " + whereClause;

            if (!string.IsNullOrEmpty(sortOrder))
                sql = sql + " ORDER BY " + sortOrder;

            using SqlConnection con = _dbConnection.GetDbConnection();

            using SqlCommand sqlCmd = con.CreateCommand();
            {
                con.Open();
                sqlCmd.Connection = con;
                sqlCmd.CommandText = sql;

                SqlDataAdapter sqlDa = new SqlDataAdapter(sqlCmd);
                    
                sqlDa = new SqlDataAdapter(sqlCmd);
                sqlDa.Fill(dtblData);

                foreach (DataRow dr in dtblData.Rows)
                {
                    SelectListItem selListItem = new SelectListItem() { Value = dr[valueField].ToString(), Text = dr[textField].ToString() };
                    dataList.Add(selListItem);
                }

                //dataList = dtblData.AsEnumerable().Select(r => r.Field<string>(fieldList)).ToList();
            }

            return dataList;

        }

        public string UpdateRecords(string tableName, DataTable tempTable, string keyField, string keyValue, string keyFieldDataType = "", string whereCondition = "")
        {
            string retrunMsg = "";
            string sql = "";
            string paralist = "";
            string fldname = "";
            string whereClause = "";
            int i = 0;

            //remove the Keyfield (i.e primary key) column as it is not updatable.
            foreach (DataColumn col in tempTable.Columns)
            {
                if (col.ColumnName.ToLower() == keyField.ToLower()) { 
                    tempTable.Columns.Remove(col);
                    break;
                }
            }

            using SqlConnection con = _dbConnection.GetDbConnection();
            con.Open();

            using SqlCommand sqlCmd = con.CreateCommand();
            {
                while (i < tempTable.Columns.Count)
                {
                    fldname = tempTable.Columns[i].ColumnName;
                    paralist = paralist + fldname + " = @" + fldname + ",";
                    i++;
                }

                //fldlist = " (" + fldlist.Remove(fldlist.Length - 1, 1) + ") ";
                paralist = paralist.Remove(paralist.Length - 1, 1);

                foreach (DataRow DR in tempTable.Rows)
                {
                    foreach (DataColumn DC in tempTable.Columns)
                    {
                        fldname = DC.ColumnName;
                        if (DC.DataType == typeof(DateTime))
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.DateTime)).Value = DR[fldname] == DBNull.Value ? DBNull.Value : Convert.ToDateTime(DR[fldname]);
                        else if (DC.DataType == typeof(int))
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.Int)).Value = DR[fldname] == DBNull.Value ? DBNull.Value : Convert.ToInt32(DR[fldname]);
                        else
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.VarChar)).Value = DR[fldname].ToString();
                    }

                    if (keyFieldDataType == "")
                        whereClause = " WHERE " + keyField + " = '" + keyValue + "'";
                    else
                        whereClause = " WHERE " + keyField + " = " + keyValue;

                    if (whereCondition.Length > 0)
                    {
                        if (whereClause.Length > 0)
                        {
                            if (whereCondition.Trim().ToUpper().StartsWith("WHERE"))
                                whereCondition = whereCondition.Trim().Remove(0, 5);

                            whereClause = whereClause + " AND " + whereCondition;
                        }
                        else
                            whereClause = whereCondition;
                    }

                    sql = "UPDATE " + tableName + " SET " + paralist + whereClause;

                    sqlCmd.CommandType = CommandType.Text;
                    sqlCmd.CommandText = sql;

                    try
                    {
                        if (sqlCmd.ExecuteNonQuery() > 0)
                            retrunMsg = "SUCCESS";
                    }
                    catch (Exception exc)
                    {
                        _logger?.LogError(exc, "Error executing UpdateRecords on table {TableName}", tableName);
                        retrunMsg = "ERROR";
                    }
                }
            }
            return retrunMsg;
        }

        public string UpdateRecords(string tableName, DataTable tempTable, string keyField)
        {
            string retrunMsg = "";
            string sql = "";
            string paralist = "";
            string fldname = "";
            string whereClause = "";
            int i = 0;

            using SqlConnection con = _dbConnection.GetDbConnection();
            con.Open();

            using SqlCommand sqlCmd = con.CreateCommand();
            {
                while (i < tempTable.Columns.Count)
                {
                    if (tempTable.Columns[i].ColumnName.ToLower() != keyField.ToLower())
                    {
                        fldname = tempTable.Columns[i].ColumnName;
                        paralist = paralist + fldname + " = @" + fldname + ",";
                    }

                    i++;
                }

                //fldlist = " (" + fldlist.Remove(fldlist.Length - 1, 1) + ") ";
                paralist = paralist.Remove(paralist.Length - 1, 1);

                foreach (DataRow DR in tempTable.Rows)
                {
                    whereClause = "";
                    sqlCmd.Parameters.Clear();

                    foreach (DataColumn DC in tempTable.Columns)
                    {
                        fldname = DC.ColumnName;
                        if (DC.DataType == typeof(DateTime))
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.DateTime)).Value = DR[fldname] == DBNull.Value ? DBNull.Value : Convert.ToDateTime(DR[fldname]);
                        else if (DC.DataType == typeof(int))
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.Int)).Value = DR[fldname] == DBNull.Value ? DBNull.Value : Convert.ToInt32(DR[fldname]);
                        else
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.VarChar)).Value = DR[fldname].ToString();

                        if (fldname.ToLower() == keyField.ToLower())
                        {
                            whereClause = keyField + " = @" + keyField;
                        }
                    }

                    whereClause = " WHERE " + whereClause;

                    sql = "UPDATE " + tableName + " SET " + paralist + whereClause;

                    sqlCmd.CommandType = CommandType.Text;
                    sqlCmd.CommandText = sql;

                    try
                    {
                        if (sqlCmd.ExecuteNonQuery() > 0)
                            retrunMsg = "SUCCESS";
                    }
                    catch (Exception exc)
                    {
                        _logger?.LogError(exc, "Error executing UpdateRecords on table {TableName}", tableName);
                        retrunMsg = "ERROR";
                    }
                }
            }
            return retrunMsg;
        }

        public string UpdateRecords(string sql)
        {
            return UpdateRecords(sql, Array.Empty<SqlParameter>());
        }

        public string UpdateRecords(string sql, params SqlParameter[] parameters)
        {
            string retrunMsg = "";

            using SqlConnection con = _dbConnection.GetDbConnection();
            con.Open();

            using SqlCommand sqlCmd = con.CreateCommand();
            {
                sqlCmd.CommandType = CommandType.Text;
                sqlCmd.CommandText = sql;

                if (parameters != null && parameters.Length > 0)
                {
                    sqlCmd.Parameters.AddRange(parameters);
                }

                try
                {
                    if (sqlCmd.ExecuteNonQuery() > 0)
                        retrunMsg = "SUCCESS";
                }
                catch (Exception exc)
                {
                    retrunMsg = exc.ToString();
                    _logger?.LogError(exc, "Error executing UpdateRecords SQL command");
                    retrunMsg = "ERROR";
                }
            }
            return retrunMsg;
        }

        public string InsertRecords(string tableName, DataTable tempTable, bool isIdentity, string identityField = "")
        {
            string retrunMsg = "";
            string sql = "";
            string fldlist = "";
            string paralist = "";
            string fldname = "";
            int i = 0;
            string valueset = "";

            //remove the identity field (i.e primary key) if table has a identity column.
            if (isIdentity)
            {
                foreach (DataColumn col in tempTable.Columns)
                {
                    if (col.ColumnName.ToLower() == identityField.ToLower())
                    {
                        tempTable.Columns.Remove(col);
                        break;
                    }
                }
            }

            using SqlConnection con = _dbConnection.GetDbConnection();
            con.Open();

            using SqlCommand sqlCmd = con.CreateCommand();
            {             
                while (i < tempTable.Columns.Count)
                {
                    fldname = tempTable.Columns[i].ColumnName;
                    fldlist = fldlist + fldname + ",";
                    paralist = paralist + "@" + fldname + ",";
                    i++;
                }

                fldlist = " (" + fldlist.Remove(fldlist.Length - 1, 1) + ") ";
                paralist = " (" + paralist.Remove(paralist.Length - 1, 1) + ") ";

                foreach (DataRow DR in tempTable.Rows)
                {
                    sqlCmd.Parameters.Clear();

                    foreach (DataColumn DC in tempTable.Columns)
                    {
                        fldname = DC.ColumnName;
                        if (DC.DataType == typeof(DateTime))
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.DateTime)).Value = DR[fldname] == DBNull.Value ? DBNull.Value : Convert.ToDateTime(DR[fldname]);
                        else if (DC.DataType == typeof(int))
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.Int)).Value = DR[fldname] == DBNull.Value ? DBNull.Value : Convert.ToInt32(DR[fldname]);
                        else
                            sqlCmd.Parameters.Add(new SqlParameter(fldname, SqlDbType.VarChar)).Value = DR[fldname].ToString();

                        valueset = valueset + DR[fldname].ToString() + "--";
                    }

                    sql = "INSERT INTO " + tableName + fldlist + " VALUES " + paralist;

                    if (isIdentity)
                        sql += "; SELECT SCOPE_IDENTITY()";

                    sqlCmd.CommandType = CommandType.Text;
                    sqlCmd.CommandText = sql;

                    try
                    {
                        if (sqlCmd.ExecuteNonQuery() > 0)
                            retrunMsg = "SUCCESS";
                    }
                    catch (Exception exc)
                    {
                       _logger?.LogError(exc, "Error executing InsertRecords on table {TableName}", tableName);
                        retrunMsg = "ERROR";
                    }
                }
            }

            return retrunMsg;
        }

        public string GetJobPositionCodeById(int jobPositionId)
        {
            string jobPositionCode = string.Empty;
            string sql = "SELECT JobPositionCode FROM JobPosition WHERE JobPositionID = @JobPositionID";
            DataTable dataTable = SelectRows(sql, new SqlParameter("@JobPositionID", SqlDbType.Int) { Value = jobPositionId });

            if (dataTable.Rows.Count > 0)
                jobPositionCode = dataTable.Rows[0][0].ToString();

            return jobPositionCode;
        }

        public string GetJobPositionName(string? intakeCode = "", string? jobPositionCode = "")
        {
            string jobPositionName = string.Empty;
             DataTable dataTable = new DataTable();
            if (!string.IsNullOrEmpty(intakeCode))
            {
                string sql = "SELECT JobPositionName FROM JobPosition INNER JOIN Intake ON Intake.JobPositionID = JobPosition.JobPositionID WHERE IntakeCode = @IntakeCode";
                dataTable = SelectRows(sql, new SqlParameter("@IntakeCode", SqlDbType.VarChar) { Value = intakeCode });
            }
            else if (!string.IsNullOrEmpty(jobPositionCode))
            {
                string sql = "SELECT JobPositionName FROM JobPosition WHERE JobPositionCode = @JobPositionCode";
                dataTable = SelectRows(sql, new SqlParameter("@JobPositionCode", SqlDbType.VarChar) { Value = jobPositionCode });
            }


            if (dataTable.Rows.Count > 0)
                jobPositionName = dataTable.Rows[0][0].ToString();

            return jobPositionName;
        }

        public DataTable GetFilteringCriteriaOfJobPosition(string intakeCode)
        {
            //string sql = "SELECT B.JobPositionCode, B.ALRequired, B.OLRequired  FROM Intake A INNER JOIN JobPosition B ON A.JobPositionID = B.JobPositionID WHERE A.IntakeCode = '" + intakeCode + "'";
            string sql = "SELECT IntakeCode, ALRequired, OLRequired FROM Intake WHERE IntakeCode = @IntakeCode";
            DataTable dataTable = SelectRows(sql, new SqlParameter("@IntakeCode", SqlDbType.VarChar) { Value = intakeCode });

            return dataTable;
        }

        public bool IsTalentPoolEnabled()
        {
            string sql = "SELECT IsTalentPoolEnabled FROM TalentPoolSettings";
            DataTable dt = SelectRows(sql);
            if (dt.Rows.Count > 0)
            {
                return dt.Rows[0]["IsTalentPoolEnabled"] != DBNull.Value && Convert.ToBoolean(dt.Rows[0]["IsTalentPoolEnabled"]);
            }
            return false; // Default to false if no setting is found
        }

        public void UpdateTalentPoolStatus(bool isEnabled)
        {
            using SqlConnection con = _dbConnection.GetDbConnection();
            using SqlCommand sqlCmd = con.CreateCommand();
            con.Open();
            // Assuming single row in settings table
            sqlCmd.CommandText = "UPDATE TalentPoolSettings SET IsTalentPoolEnabled = @isEnabled";
            sqlCmd.Parameters.Add(new SqlParameter("@isEnabled", SqlDbType.Bit)).Value = isEnabled;
            
            int rowsAffected = sqlCmd.ExecuteNonQuery();
            if (rowsAffected == 0)
            {
                // If table is empty, insert
                sqlCmd.CommandText = "INSERT INTO TalentPoolSettings (IsTalentPoolEnabled) VALUES (@isEnabled)";
                sqlCmd.ExecuteNonQuery();
            }
        }
    }
}
