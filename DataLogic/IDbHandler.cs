using System;
using System.Collections.Generic;
using System.Data;

namespace DBAccess
{
    public interface IDbHandler
    {
        DataTable ExecuteDataTable(string query, Dictionary<string, object> parameters = null, CommandType commandType = CommandType.Text);
        int ExecuteNonQuery(string query, Dictionary<string, object> parameters = null, CommandType commandType = CommandType.Text);
        object ExecuteScalar(string query, Dictionary<string, object> parameters = null, CommandType commandType = CommandType.Text);

        Task ExecuteNonQueryAsync(string storedProcedure, Dictionary<string, object> parameters, CommandType commandType);
    }
}
