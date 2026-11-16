using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Aura
{
    // SQLite through winsqlite3.dll, which is part of Windows 10 and 11 (no extra files).
    static class Sqlite
    {
        const string L = "winsqlite3";
        const CallingConvention CC = CallingConvention.Winapi;
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, out IntPtr tail);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_step(IntPtr stmt);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_reset(IntPtr stmt);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_clear_bindings(IntPtr stmt);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_bind_int64(IntPtr stmt, int i, long v);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_bind_double(IntPtr stmt, int i, double v);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_bind_text(IntPtr stmt, int i, byte[] v, int n, IntPtr destructor);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_bind_blob(IntPtr stmt, int i, byte[] v, int n, IntPtr destructor);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_bind_null(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_column_type(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern long sqlite3_column_int64(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern double sqlite3_column_double(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern IntPtr sqlite3_column_blob(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_column_bytes(IntPtr stmt, int i);
        [DllImport(L, CallingConvention = CC)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(L, CallingConvention = CC)] public static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport(L, CallingConvention = CC)] public static extern int sqlite3_changes(IntPtr db);
        [DllImport(L, CallingConvention = CC)] public static extern IntPtr sqlite3_libversion();

        public static readonly IntPtr TRANSIENT = new IntPtr(-1);

        public static string Utf8(IntPtr p, int n = -1)
        {
            if (p == IntPtr.Zero) return null;
            if (n < 0) { n = 0; while (Marshal.ReadByte(p, n) != 0) n++; }
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }
        public static byte[] Z(string s) => Encoding.UTF8.GetBytes((s ?? "") + "\0");
        public static string Version() { try { return Utf8(sqlite3_libversion()); } catch { return null; } }
    }

    sealed class DbException : Exception { public DbException(string m) : base(m) { } }

    sealed class Db : IDisposable
    {
        IntPtr h;
        public readonly object Lock = new object();
        public string Path { get; }

        public Db(string path, bool readOnly = false)
        {
            Path = path;
            int flags = readOnly ? 0x01 : (0x02 | 0x04);
            flags |= 0x10000;   // full mutex: safe to use from the scan thread too
            int rc = Sqlite.sqlite3_open_v2(Sqlite.Z(path), out h, flags, IntPtr.Zero);
            if (rc != 0)
            {
                var msg = h != IntPtr.Zero ? Sqlite.Utf8(Sqlite.sqlite3_errmsg(h)) : "code " + rc;
                if (h != IntPtr.Zero) Sqlite.sqlite3_close_v2(h);
                h = IntPtr.Zero;
                throw new DbException("Couldn't open " + path + ": " + msg);
            }
            Sqlite.sqlite3_busy_timeout(h, 5000);
            if (!readOnly) { try { Exec("PRAGMA journal_mode=WAL"); Exec("PRAGMA synchronous=NORMAL"); } catch { } }
        }

        public string Error => Sqlite.Utf8(Sqlite.sqlite3_errmsg(h));
        public long LastId => Sqlite.sqlite3_last_insert_rowid(h);
        public int Changes => Sqlite.sqlite3_changes(h);

        public Stmt Prepare(string sql)
        {
            var b = Sqlite.Z(sql);
            int rc = Sqlite.sqlite3_prepare_v2(h, b, b.Length, out IntPtr st, out _);
            if (rc != 0) throw new DbException(Error + "\n" + sql);
            return new Stmt(this, st);
        }

        public int Exec(string sql, params object[] args)
        {
            lock (Lock)
            {
                using (var st = Prepare(sql))
                {
                    st.Bind(args);
                    while (st.Step()) { }
                    return Changes;
                }
            }
        }

        // several statements separated by ';' (no parameters)
        public void Script(string sql)
        {
            foreach (var part in sql.Split(';'))
                if (part.Trim().Length > 0) Exec(part);
        }

        public object Scalar(string sql, params object[] args)
        {
            lock (Lock)
            {
                using (var st = Prepare(sql))
                {
                    st.Bind(args);
                    return st.Step() ? st.Value(0) : null;
                }
            }
        }

        public List<object[]> Rows(string sql, params object[] args)
        {
            var list = new List<object[]>();
            lock (Lock)
            {
                using (var st = Prepare(sql))
                {
                    st.Bind(args);
                    int n = -1;
                    while (st.Step())
                    {
                        if (n < 0) n = st.Columns;
                        var r = new object[n];
                        for (int i = 0; i < n; i++) r[i] = st.Value(i);
                        list.Add(r);
                    }
                }
            }
            return list;
        }

        public void Tx(Action a)
        {
            lock (Lock)
            {
                Exec("BEGIN");
                try { a(); Exec("COMMIT"); }
                catch { try { Exec("ROLLBACK"); } catch { } throw; }
            }
        }

        public void Dispose()
        {
            if (h != IntPtr.Zero) { Sqlite.sqlite3_close_v2(h); h = IntPtr.Zero; }
        }
    }

    sealed class Stmt : IDisposable
    {
        readonly Db db;
        IntPtr st;
        internal Stmt(Db db, IntPtr st) { this.db = db; this.st = st; }

        public void Bind(params object[] args)
        {
            Sqlite.sqlite3_reset(st);
            Sqlite.sqlite3_clear_bindings(st);
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                int k = i + 1;
                switch (args[i])
                {
                    case null: Sqlite.sqlite3_bind_null(st, k); break;
                    case string s: { var b = Encoding.UTF8.GetBytes(s); Sqlite.sqlite3_bind_text(st, k, b, b.Length, Sqlite.TRANSIENT); break; }
                    case byte[] b: Sqlite.sqlite3_bind_blob(st, k, b, b.Length, Sqlite.TRANSIENT); break;
                    case bool v: Sqlite.sqlite3_bind_int64(st, k, v ? 1 : 0); break;
                    case int v: Sqlite.sqlite3_bind_int64(st, k, v); break;
                    case long v: Sqlite.sqlite3_bind_int64(st, k, v); break;
                    case double v: Sqlite.sqlite3_bind_double(st, k, v); break;
                    case float v: Sqlite.sqlite3_bind_double(st, k, v); break;
                    default: { var b = Encoding.UTF8.GetBytes(args[i].ToString()); Sqlite.sqlite3_bind_text(st, k, b, b.Length, Sqlite.TRANSIENT); break; }
                }
            }
        }

        public bool Step()
        {
            int rc = Sqlite.sqlite3_step(st);
            if (rc == 100) return true;     // a row
            if (rc == 101) return false;    // done
            throw new DbException(db.Error);
        }

        public int Columns => Sqlite.sqlite3_column_count(st);

        public object Value(int i)
        {
            switch (Sqlite.sqlite3_column_type(st, i))
            {
                case 1: return Sqlite.sqlite3_column_int64(st, i);
                case 2: return Sqlite.sqlite3_column_double(st, i);
                case 3: return Sqlite.Utf8(Sqlite.sqlite3_column_text(st, i), Sqlite.sqlite3_column_bytes(st, i));
                case 4:
                    {
                        int n = Sqlite.sqlite3_column_bytes(st, i);
                        var b = new byte[n];
                        if (n > 0) Marshal.Copy(Sqlite.sqlite3_column_blob(st, i), b, 0, n);
                        return b;
                    }
                default: return null;
            }
        }

        public void Dispose()
        {
            if (st != IntPtr.Zero) { Sqlite.sqlite3_finalize(st); st = IntPtr.Zero; }
        }
    }

    static class Conv
    {
        public static long L(object o) => o is long l ? l : o is double d ? (long)d : o is string s && long.TryParse(s, out var x) ? x : 0;
        public static double D(object o) => o is double d ? d : o is long l ? l : o is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
        public static string S(object o) => o == null ? "" : o is string s ? s : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
    }
}
