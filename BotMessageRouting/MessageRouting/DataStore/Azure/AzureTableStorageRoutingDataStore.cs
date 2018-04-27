﻿using BotMessageRouting.Models.Azure;
using Microsoft.Bot.Schema;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Underscore.Bot.Models;
using Underscore.Bot.Utils;

namespace Underscore.Bot.MessageRouting.DataStore.Azure
{
    /// <summary>
    /// Routing data manager that stores the data in Azure Table Storage.
    /// See IRoutingDataManager and AbstractRoutingDataManager for general documentation of
    /// properties and methods.
    /// </summary>
    [Serializable]
    public class AzureTableStorageRoutingDataStore : IRoutingDataStore
    {
        protected const string partitionKey = "botHandOff";
        protected const string TableNameBotInstances = "BotInstances";
        protected const string TableNameUsers = "Users";
        protected const string TableNameAggregationChannels = "AggregationChannels";
        protected const string TableNameConnectionRequests = "ConnectionRequests";
        protected const string TableNameConnections = "Connections";
        protected const string PartitionKey = "PartitionKey";

        protected CloudTable _botInstancesTable;
        protected CloudTable _usersTable;
        protected CloudTable _aggregationChannelsTable;
        protected CloudTable _connectionRequestsTable;
        protected CloudTable _connectionsTable;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="connectionString">The connection string associated with an Azure Table Storage.</param>
        /// <param name="globalTimeProvider">The global time provider for providing the current
        /// time for various events such as when a connection is requested.</param>
        public AzureTableStorageRoutingDataStore(string connectionString, 
            GlobalTimeProvider globalTimeProvider = null): base()
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("The connection string cannot be null or empty");
            }

            _botInstancesTable = AzureStorageHelper.GetTable(connectionString, TableNameBotInstances);
            _usersTable = AzureStorageHelper.GetTable(connectionString, TableNameUsers);
            _aggregationChannelsTable = AzureStorageHelper.GetTable(connectionString, TableNameAggregationChannels);
            _connectionRequestsTable = AzureStorageHelper.GetTable(connectionString, TableNameConnectionRequests);
            _connectionsTable = AzureStorageHelper.GetTable(connectionString, TableNameConnections);

            MakeSureTablesExistAsync();
        }

        #region Get region
        public IList<ConversationReference> GetUsers()
        {
            var entities = GetAllEntitiesFromTable(_usersTable).Result;
            return GetAllConversationReferencesFromEntities(entities);
        }

        public IList<ConversationReference> GetBotInstances()
        {
            var entities = GetAllEntitiesFromTable(_botInstancesTable).Result;
            return GetAllConversationReferencesFromEntities(entities);
        }

        public IList<ConversationReference> GetAggregationChannels()
        {
            var entities = GetAllEntitiesFromTable(_aggregationChannelsTable).Result;
            return GetAllConversationReferencesFromEntities(entities);
        }

        public IList<ConnectionRequest> GetConnectionRequests()
        {
            var entities = GetAllEntitiesFromTable(_connectionRequestsTable).Result;

            var connectionRequests = new List<ConnectionRequest>();
            foreach (RoutingDataEntity entity in entities)
            {
                var connectionRequest = 
                    JsonConvert.DeserializeObject<ConnectionRequest>(entity.Body);
                connectionRequests.Add(connectionRequest);
            }
            return connectionRequests;
        }

        public IList<Connection> GetConnections()
        {
            var entities = GetAllEntitiesFromTable(_connectionsTable).Result;

            var connections = new List<Connection>();
            foreach (RoutingDataEntity entity in entities)
            {
                var connection = 
                    JsonConvert.DeserializeObject<Connection>(entity.Body);
                connections.Add(connection);
            }
            return connections;
        }
        #endregion

        #region Add region
        public bool AddConversationReference(ConversationReference conversationReference)
        {
            CloudTable table;
            if (conversationReference.Bot != null)
                table = _botInstancesTable;
            else table = _usersTable;

            string rowKey = conversationReference.Conversation.Id;
            string body = JsonConvert.SerializeObject(conversationReference);

            return InsertEntityToTable(rowKey, body, table);
        }

        public bool AddAggregationChannel(ConversationReference aggregationChannel)
        {
            string rowKey = aggregationChannel.Conversation.Id;
            string body = JsonConvert.SerializeObject(aggregationChannel);

            return InsertEntityToTable(rowKey, body, _aggregationChannelsTable);
        }

        public bool AddConnectionRequest(ConnectionRequest connectionRequest)
        {
            string rowKey = connectionRequest.Requestor.Conversation.Id;
            string body = JsonConvert.SerializeObject(connectionRequest);

            return InsertEntityToTable(rowKey, body, _connectionRequestsTable);
        }

        public bool AddConnection(Connection connection)
        {
            string rowKey = connection.ConversationReference1.Conversation.Id +
                connection.ConversationReference2.Conversation.Id;
            string body = JsonConvert.SerializeObject(connection);

            return InsertEntityToTable(rowKey, body, _connectionsTable);
        }
        #endregion

        #region Remove region
        public bool RemoveConversationReference(ConversationReference conversationReference)
        {
            CloudTable table;
            if (conversationReference.Bot != null)
                table = _botInstancesTable;
            else table = _usersTable;

            string rowKey = conversationReference.Conversation.Id;
            return AzureStorageHelper.DeleteEntryAsync<RoutingDataEntity>(
                table, partitionKey, rowKey).Result;
        }

        public bool RemoveAggregationChannel(ConversationReference aggregationChannel)
        {
            string rowKey = aggregationChannel.Conversation.Id;
            return AzureStorageHelper.DeleteEntryAsync<RoutingDataEntity>(
                _aggregationChannelsTable, partitionKey, rowKey).Result;
        }

        public bool RemoveConnectionRequest(ConnectionRequest connectionRequest)
        {
            string rowKey = connectionRequest.Requestor.Conversation.Id;
            return AzureStorageHelper.DeleteEntryAsync<RoutingDataEntity>(
                _connectionRequestsTable, partitionKey, rowKey).Result;
        }

        public bool RemoveConnection(Connection connection)
        {
            string rowKey = connection.ConversationReference1.Conversation.Id +
                connection.ConversationReference2.Conversation.Id;
            return AzureStorageHelper.DeleteEntryAsync<RoutingDataEntity>(
                _connectionsTable, partitionKey, rowKey).Result;
        }
        #endregion

        #region Validators and helpers
        /// <summary>
        /// Makes sure the required tables exist.
        /// </summary>
        protected virtual async void MakeSureTablesExistAsync()
        {
            CloudTable[] cloudTables =
            {
                _botInstancesTable,
                _usersTable,
                _aggregationChannelsTable,
                _connectionRequestsTable,
                _connectionsTable
            };

            foreach (CloudTable cloudTable in cloudTables)
            {
                try
                {
                    await cloudTable.CreateIfNotExistsAsync();
                    Debug.WriteLine($"Table '{cloudTable.Name}' created or did already exist");
                }
                catch (StorageException e)
                {
                    Debug.WriteLine($"Failed to create table '{cloudTable.Name}' (perhaps it already exists): {e.Message}");
                }
            }
        }

        private List<ConversationReference> GetAllConversationReferencesFromEntities(IList<RoutingDataEntity> entities)
        {
            var conversationReferences = new List<ConversationReference>();
            foreach (RoutingDataEntity entity in entities)
            {
                var conversationReference =
                    JsonConvert.DeserializeObject<ConversationReference>(entity.Body);
                conversationReferences.Add(conversationReference);
            }
            return conversationReferences;
        }

        private async Task<IList<RoutingDataEntity>> GetAllEntitiesFromTable(CloudTable table)
        {
            var query = new TableQuery<RoutingDataEntity>()
                .Where(TableQuery.GenerateFilterCondition(
                    "PartitionKey", QueryComparisons.Equal, partitionKey));
            return await table.ExecuteTableQueryAsync(query);
        }

        private static bool InsertEntityToTable(string rowKey, string body, CloudTable table)
        {
            return AzureStorageHelper.InsertAsync<RoutingDataEntity>(table, 
                new RoutingDataEntity()
                {
                    Body = body, PartitionKey = partitionKey, RowKey = rowKey
                }).Result;
        }
        #endregion
    }
}