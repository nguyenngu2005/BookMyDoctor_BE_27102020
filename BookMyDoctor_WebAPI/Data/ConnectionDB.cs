//Magic. Don't touch
using System;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace BookMyDoctor_WebAPI.Data
{
    /// DB helper thuần ADO.NET dùng cho raw SQL/stored procedures.
    /// Gợi ý: dùng song song với EF Core (DbContext) cho CRUD thường ngày.
    public class ConnectionDB : IDisposable
    {
        private readonly string _connectionString;
        private SqlConnection? _sharedConnection;
        private SqlTransaction? _currentTransaction;

        public ConnectionDB(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection");
        }
        /// Mở connection mới (hoặc trả về connection hiện có khi đang trong transaction)
        private async Task<SqlConnection> GetOpenConnectionAsync()
        {
            if (_currentTransaction != null && _sharedConnection != null)
            {
                // đang trong transaction: dùng lại shared connection
                if (_sharedConnection.State != ConnectionState.Open)
                    await _sharedConnection.OpenAsync();
                return _sharedConnection;
            }

            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return conn;
        }
        /// Bắt đầu transaction (không lồng nhau)
        public async Task BeginTransactionAsync(IsolationLevel iso = IsolationLevel.ReadCommitted)
        {
            if (_currentTransaction != null)
                throw new InvalidOperationException("Transaction đã tồn tại. Hãy Commit/Rollback trước.");

            _sharedConnection = new SqlConnection(_connectionString);
            await _sharedConnection.OpenAsync();
            _currentTransaction = _sharedConnection.BeginTransaction(iso);
        }
        public Task CommitAsync()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("Chưa bắt đầu transaction.");
            _currentTransaction.Commit();
            CleanupTransaction();
            return Task.CompletedTask;
        }
        public Task RollbackAsync()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("Chưa bắt đầu transaction.");
            _currentTransaction.Rollback();
            CleanupTransaction();
            return Task.CompletedTask;
        }
        private void CleanupTransaction()
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;

            _sharedConnection?.Close();
            _sharedConnection?.Dispose();
            _sharedConnection = null;
        }
        /// Health check đơn giản: có kết nối DB không.
        public async Task<bool> CanConnectAsync()
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
        /// SELECT trả về DataTable (dễ debug/log, không mapping model)
        public async Task<DataTable> QueryAsync(string sql, Dictionary<string, object?>? parameters = null,
                                                CommandType commandType = CommandType.Text, int timeout = 30)
        {
            var dt = new DataTable();
            var conn = await GetOpenConnectionAsync();
            var shouldDispose = _currentTransaction == null; // nếu không trong tran → tự dispose

            try
            {
                using var cmd = new SqlCommand(sql, conn)
                {
                    CommandType = commandType,
                    CommandTimeout = timeout,
                    Transaction = _currentTransaction
                };

                AddParameters(cmd, parameters);

                using var adapter = new SqlDataAdapter(cmd);
                adapter.Fill(dt);
                return dt;
            }
            finally
            {
                if (shouldDispose)
                    await DisposeConnectionAsync(conn);
            }
        }
        /// INSERT/UPDATE/DELETE → số dòng ảnh hưởng
        public async Task<int> ExecuteAsync(string sql, Dictionary<string, object?>? parameters = null,
                                            CommandType commandType = CommandType.Text, int timeout = 30)
        {
            var conn = await GetOpenConnectionAsync();
            var shouldDispose = _currentTransaction == null;

            try
            {
                using var cmd = new SqlCommand(sql, conn)
                {
                    CommandType = commandType,
                    CommandTimeout = timeout,
                    Transaction = _currentTransaction
                };
                AddParameters(cmd, parameters);
                return await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                if (shouldDispose)
                    await DisposeConnectionAsync(conn);
            }
        }
        /// Trả về giá trị đơn (ví dụ SELECT COUNT(*), SCOPE_IDENTITY(), …)
        public async Task<object?> ScalarAsync(string sql, Dictionary<string, object?>? parameters = null,
                                               CommandType commandType = CommandType.Text, int timeout = 30)
        {
            var conn = await GetOpenConnectionAsync();
            var shouldDispose = _currentTransaction == null;

            try
            {
                using var cmd = new SqlCommand(sql, conn)
                {
                    CommandType = commandType,
                    CommandTimeout = timeout,
                    Transaction = _currentTransaction
                };
                AddParameters(cmd, parameters);
                return await cmd.ExecuteScalarAsync();
            }
            finally
            {
                if (shouldDispose)
                    await DisposeConnectionAsync(conn);
            }
        }
        private static void AddParameters(SqlCommand cmd, Dictionary<string, object?>? parameters)
        {
            if (parameters == null) return;
            foreach (var kv in parameters)
            {
                var name = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                cmd.Parameters.AddWithValue(name, kv.Value ?? DBNull.Value);
            }
        }
        private static async Task DisposeConnectionAsync(SqlConnection conn)
        {
            await conn.CloseAsync();
            await conn.DisposeAsync();
        }
        public void Dispose()
        {
            _currentTransaction?.Dispose();
            _sharedConnection?.Dispose();
        }
    }
}
