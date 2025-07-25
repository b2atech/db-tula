﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace B2A.DbTula.Core.Abstractions
{
    public interface IDatabaseConnection
    {
        Task<DataTable> ExecuteQueryAsync(string query);
        Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters);
        Task ExecuteCommandAsync(string sqlCommand);

        // Optional synchronous methods — include only if you really need them
        DataTable ExecuteQuery(string query);
        void ExecuteCommand(string sqlCommand);
    }
}
