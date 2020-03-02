﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FAnsi.Discovery;
using Microservices.IsIdentifiable.Reporting;

namespace IsIdentifiableReviewer.Out
{
    class RowUpdater
    {
        Dictionary<DiscoveredTable,DiscoveredColumn> _primaryKeys = new Dictionary<DiscoveredTable, DiscoveredColumn>();

        public void Update(Target target, Failure failure)
        {
            var server = target.Discover();
            var syntax = server.GetQuerySyntaxHelper();

            //the fully specified name e.g. [mydb]..[mytbl]
            string tableName = failure.Resource;

            var tokens = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries);

            var db = tokens.First();
            tableName = tokens.Last();

            if(string.IsNullOrWhiteSpace(db) || string.IsNullOrWhiteSpace(tableName) || string.Equals(db , tableName))
                throw new NotSupportedException($"Could not understand table name {failure.Resource}, maybe it is not full specified with a valid database and table name?");

            db = syntax.GetRuntimeName(db);
            tableName = syntax.GetRuntimeName(tableName);

            DiscoveredTable table = server.ExpectDatabase(db).ExpectTable(tableName);

            //if we've never seen this table before
            if (!_primaryKeys.ContainsKey(table))
            {
                var pk = table.DiscoverColumns().SingleOrDefault(k => k.IsPrimaryKey);
                _primaryKeys.Add(table,pk);
            }

            using (var con = server.GetConnection())
            {
                con.Open();

                string sql =
                $@"update {failure.Resource} 
                SET {failure.ProblemField} = 
                REPLACE({failure.ProblemField},'{syntax.Escape(failure.ProblemValue)}', 'SMI_REDACTED')
                WHERE {_primaryKeys[table].GetFullyQualifiedName()} = '{syntax.Escape(failure.ResourcePrimaryKey)}'";

                var cmd = server.GetCommand(sql, con);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
