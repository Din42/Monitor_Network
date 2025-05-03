using NLog;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;


namespace Monitor_Network
{
    internal class Database
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly string _connectionString;

        static Database()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["SqlServer"].ConnectionString;

        }

        public static void LogVisit(string domain, string processName, string ipAddress)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    var command = new SqlCommand(
                        "INSERT INTO BrowsingHistory (Domain, ProcessName, IpAddress, VisitTime)" +
                        "VALUES (@domain, @process, @ip, @time)",
                        connection);

                    command.Parameters.AddWithValue("@domain", domain);
                    command.Parameters.AddWithValue("@process", processName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ip", ipAddress ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@time", DateTime.Now);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error(ex, "Ошибка запили в БД");
            }           
        }

         public static DataTable GetNetworkHistory()
        {
            var table = new DataTable();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                using (var adapter = new SqlDataAdapter("SELECT * FROM BrowsingHistory ORDER BY VisitTime DESC", connection))
                {
                    adapter.Fill(table);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ошибка загрузки истории");
            }
            return table;
        }
    }
}
