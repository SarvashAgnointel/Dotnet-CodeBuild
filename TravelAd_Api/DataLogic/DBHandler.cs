using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;

namespace DBAccess
{
    public class DbHandler : IDbHandler
    {
        private readonly string _connectionString;

        public DbHandler(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public DataTable ExecuteDataTable(string query, Dictionary<string, object> parameters = null, CommandType commandType = CommandType.Text)
        {
            using SqlConnection cnn = new SqlConnection(_connectionString);
            DataTable temp = new DataTable();
            using SqlCommand cmd = new SqlCommand(query, cnn);
            cmd.CommandType = commandType;

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
            }

            using SqlDataAdapter da = new SqlDataAdapter(cmd);
            try
            {
                da.Fill(temp);
            }
            catch (Exception)
            {
                throw;
            }
            return temp;
        }

        public int ExecuteNonQuery(string query, Dictionary<string, object> parameters = null, CommandType commandType = CommandType.Text)
        {
            using SqlConnection cnn = new SqlConnection(_connectionString);
            using SqlCommand cmd = new SqlCommand(query, cnn);
            cmd.CommandType = commandType;

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
            }

            try
            {
                cnn.Open();
                return cmd.ExecuteNonQuery();
            }
            catch (Exception)
            {
                throw;
            }
        }

        public object ExecuteScalar(string query, Dictionary<string, object> parameters = null, CommandType commandType = CommandType.Text)
        {
            using SqlConnection cnn = new SqlConnection(_connectionString);
            using SqlCommand cmd = new SqlCommand(query, cnn);
            cmd.CommandType = commandType;

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
            }

            try
            {
                cnn.Open();
                return cmd.ExecuteScalar();
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task ExecuteNonQueryAsync(string storedProcedure, Dictionary<string, object> parameters, CommandType commandType)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(storedProcedure, connection))
                {
                    command.CommandType = commandType;

                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }

                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
