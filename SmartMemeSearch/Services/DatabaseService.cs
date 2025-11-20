using Microsoft.Data.Sqlite;
using SmartMemeSearch.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace SmartMemeSearch.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService()
        {
            string baseDir = AppContext.BaseDirectory;
            _dbPath = Path.Combine(baseDir, "meme_embeddings.db");

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

        public void RemoveMissingFiles(IEnumerable<string> existingFiles)
        {
            using var con = new SqliteConnection($"Data Source={_dbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText =
            @"
            DELETE FROM embeddings
            WHERE file_path NOT IN (SELECT value FROM json_each($files));
            ";

            cmd.Parameters.AddWithValue("$files", System.Text.Json.JsonSerializer.Serialize(existingFiles));
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
    }
}
