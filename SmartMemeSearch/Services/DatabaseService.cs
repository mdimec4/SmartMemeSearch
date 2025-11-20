using Microsoft.Data.Sqlite;
using SmartMemeSearch.Models;
using System.Collections.Generic;


namespace SmartMemeSearch.Services
{
    public class DatabaseService
    {
        public IEnumerable<MemeEmbedding> GetAllEmbeddings()
        {
            // TODO: Load from SQLite
            return new List<MemeEmbedding>();
        }
    }
}