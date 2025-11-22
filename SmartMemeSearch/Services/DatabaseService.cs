using Microsoft.Data.Sqlite;
using SmartMemeSearch.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace SmartMemeSearch.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;

        // Bound to UI folder manager
        public ObservableCollection<string> Folders { get; } = new();

        public DatabaseService()
        {
            string localState =
                Windows.Storage.ApplicationData.Current.LocalFolder.Path;

            _dbPath = Path.Combine(localState, "memes.db");
            Initialize();
        }

        // ------------------------------------------------------------
        // DB INITIALIZATION
        // ------------------------------------------------------------
        private void Initialize()
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS embeddings (
                file_path TEXT PRIMARY KEY,
                vector BLOB NOT NULL,
                ocr_text TEXT,
                last_modified INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_ocr_text ON embeddings(ocr_text);

            CREATE TABLE IF NOT EXISTS folders (
                path TEXT PRIMARY KEY
            );
            ";

            cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------
        // INSERT OR UPDATE RECORD
        // ------------------------------------------------------------
        public void InsertOrUpdate(MemeEmbedding item)
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText =
            @"
            INSERT INTO embeddings (file_path, vector, ocr_text, last_modified)
            VALUES ($path, $vec, $ocr, $mod)
            ON CONFLICT(file_path) DO UPDATE SET
                vector = excluded.vector,
                ocr_text = excluded.ocr_text,
                last_modified = excluded.last_modified;
            ";

            cmd.Parameters.AddWithValue("$path", item.FilePath);
            cmd.Parameters.AddWithValue("$vec", FloatArrayToBytes(item.Vector));
            cmd.Parameters.AddWithValue("$ocr", (object?)item.OcrText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mod", item.LastModified);

            cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------
        // LOAD ALL EMBEDDINGS
        // ------------------------------------------------------------
        public IEnumerable<MemeEmbedding> GetAllEmbeddings()
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT file_path, vector, ocr_text, last_modified FROM embeddings;";

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                yield return new MemeEmbedding
                {
                    FilePath = reader.GetString(0),
                    Vector = BytesToFloatArray((byte[])reader["vector"]),
                    OcrText = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    LastModified = reader.GetInt64(3)
                };
            }
        }

        // ------------------------------------------------------------
        // REMOVE ENTRIES FOR DELETED FILES
        // ------------------------------------------------------------
        public void RemoveMissingFiles(IEnumerable<string> existingFiles, string rootFolder)
        {
            var set = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);

            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var select = con.CreateCommand();
            select.CommandText = "SELECT file_path FROM embeddings;";
            var toDelete = new List<string>();

            using (var r = select.ExecuteReader())
            {
                while (r.Read())
                {
                    string path = r.GetString(0);

                    // MUST only remove files inside this folder
                    if (path.StartsWith(rootFolder + Path.DirectorySeparatorChar,
                                        StringComparison.OrdinalIgnoreCase)
                        && !set.Contains(path))
                    {
                        toDelete.Add(path);
                    }
                }
            }

            foreach (var path in toDelete)
            {
                var del = con.CreateCommand();
                del.CommandText = "DELETE FROM embeddings WHERE file_path = $p;";
                del.Parameters.AddWithValue("$p", path);
                del.ExecuteNonQuery();

                ThumbnailCache.Delete(path);
            }
        }


        // ------------------------------------------------------------
        // FOLDER MANAGEMENT
        // ------------------------------------------------------------
        public void AddFolder(string path)
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO folders (path) VALUES ($p);";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }

        public List<string> GetFolders()
        {
            var list = new List<string>();

            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT path FROM folders;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString(0));

            return list;
        }

        public void LoadFolders()
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT path FROM folders";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                Folders.Add(reader.GetString(0));
        }

        public void SetFolders(List<string> folders)
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var tr = con.BeginTransaction();

            var clear = con.CreateCommand();
            clear.CommandText = "DELETE FROM folders";
            clear.ExecuteNonQuery();

            foreach (var f in folders)
            {
                var cmd = con.CreateCommand();
                cmd.CommandText = "INSERT INTO folders (path) VALUES ($p)";
                cmd.Parameters.AddWithValue("$p", f);
                cmd.ExecuteNonQuery();
            }

            tr.Commit();

            // Update cached observable list
            Folders.Clear();
            foreach (var f in folders)
                Folders.Add(f);
        }

        public void RemoveOrphanedEntries()
        {
            var validRoots = Folders.ToList();

            var all = GetAllEmbeddings();
            foreach (var e in all)
            {
                bool inside = validRoots.Any(root =>
                    e.FilePath.StartsWith(root + Path.DirectorySeparatorChar));

                if (!inside)
                {
                    RemoveEmbedding(e.FilePath);
                    ThumbnailCache.Delete(e.FilePath);
                }
            }
        }

        // ------------------------------------------------------------
        // REMOVE SINGLE EMBEDDING  ← FIXED & INSIDE CLASS
        // ------------------------------------------------------------
        public void RemoveEmbedding(string filePath)
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM embeddings WHERE file_path = $p;";
            cmd.Parameters.AddWithValue("$p", filePath);
            cmd.ExecuteNonQuery();
        }

        public void RemoveEmbeddingWithinTransaction(SqliteConnection con, string filePath)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM embeddings WHERE file_path = $p";
            cmd.Parameters.AddWithValue("$p", filePath);
            cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------
        // HELPERS — FLOAT[] <-> BYTE[]
        // ------------------------------------------------------------
        private static byte[] FloatArrayToBytes(float[] arr)
        {
            byte[] bytes = new byte[arr.Length * sizeof(float)];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static float[] BytesToFloatArray(byte[] bytes)
        {
            float[] arr = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
            return arr;
        }

        public SqliteConnection OpenConnection()
        {
            var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();
            return con;
        }
    }
}
