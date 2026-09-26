using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>Mot don hang trong bang: ten + cac doi tuong da quet cho no.</summary>
    internal sealed class NestingOrderEntry
    {
        public string Name = string.Empty;

        public readonly List<ObjectId> Ids = new List<ObjectId>();

        /// <summary>So chi tiet nhan dang duoc tu rieng luot quet nay (xem truoc).</summary>
        public int PartCount { get; set; }

        /// <summary>Tong SL cua rieng luot quet nay (xem truoc).</summary>
        public int Quantity { get; set; }

        public bool Scanned { get { return Ids.Count > 0; } }
    }

    /// <summary>
    /// LUAT cua bang don hang, tach han khoi giao dien.
    ///
    /// De o day chu khong nam trong <see cref="NestingOrderForm"/> vi day moi la phan co the
    /// sai: ten trung nhau, ten de trong, quet lan hai de len luot quet truoc, xoa nham dong.
    /// Tach ra thi kiem thu duoc tung luat mot ma khong can mo hop thoai.
    /// </summary>
    internal sealed class NestingOrderList
    {
        private readonly List<NestingOrderEntry> _items = new List<NestingOrderEntry>();

        public NestingOrderList()
        {
            Add();
        }

        public IList<NestingOrderEntry> Items { get { return _items; } }

        public int Count { get { return _items.Count; } }

        public NestingOrderEntry this[int index] { get { return _items[index]; } }

        /// <summary>Them mot dong moi voi ten mac dinh chua bi trung.</summary>
        public NestingOrderEntry Add()
        {
            string name;
            int n = _items.Count + 1;
            do
            {
                name = "DON-" + n.ToString("00", CultureInfo.InvariantCulture);
                n++;
            }
            while (IndexOfName(name, -1) >= 0);

            NestingOrderEntry entry = new NestingOrderEntry { Name = name };
            _items.Add(entry);
            return entry;
        }

        /// <summary>
        /// Xoa mot dong. Bang khong bao gio duoc rong: xoa dong cuoi thi tu tao lai mot dong
        /// trong, de nguoi dung con cho ma go.
        /// </summary>
        public void Remove(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            _items.RemoveAt(index);
            if (_items.Count == 0) Add();
        }

        /// <summary>Dat ten cho mot dong. Khoang trang thua o hai dau bi cat bo.</summary>
        public void SetName(int index, string name)
        {
            if (index < 0 || index >= _items.Count) return;
            _items[index].Name = (name ?? string.Empty).Trim();
        }

        /// <summary>So thu tu (bat dau tu 1) cua dong dau tien trung ten voi dong nay, hoac 0.</summary>
        public int DuplicateRowOf(int index)
        {
            if (index < 0 || index >= _items.Count) return 0;
            if (string.IsNullOrEmpty(_items[index].Name)) return 0;
            int other = IndexOfName(_items[index].Name, index);
            return other < 0 ? 0 : other + 1;
        }

        /// <summary>Null = quet duoc. Khac null = ly do khong quet duoc, doc len cho nguoi dung.</summary>
        public string WhyCannotScan(int index)
        {
            if (index < 0 || index >= _items.Count) return "Dong khong hop le.";
            if (string.IsNullOrEmpty(_items[index].Name)) return "Nhap ten don hang truoc khi quet.";

            int duplicate = DuplicateRowOf(index);
            if (duplicate > 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Ten don \"{0}\" da co o dong {1}. Dat ten khac, hoac quet them vao dung dong da co.",
                    _items[index].Name, duplicate);
            }

            return null;
        }

        /// <summary>
        /// Nhan ket qua mot luot quet.
        ///
        /// <paramref name="append"/> = true thi quet THEM vao nhung gi da co; false thi bo het
        /// va lay lai tu dau. Khong bao gio tu quyet - ben goi phai hoi nguoi dung truoc, vi
        /// de mat mot luot quet cu ma khong bao la loi nang.
        ///
        /// Doi tuong trung nhau chi tinh mot lan.
        /// </summary>
        public void ApplyScan(int index, IEnumerable<ObjectId> ids, bool append)
        {
            if (index < 0 || index >= _items.Count || ids == null) return;

            NestingOrderEntry entry = _items[index];
            if (!append) entry.Ids.Clear();
            foreach (ObjectId id in ids)
            {
                if (!entry.Ids.Contains(id)) entry.Ids.Add(id);
            }
        }

        /// <summary>
        /// Chot bang truoc khi chay: bo cac dong chua quet, kiem ten, va tra ve loi neu co.
        ///
        /// Null = chot duoc. Sau khi chot, <see cref="Items"/> chi con cac dong da quet.
        ///
        /// Chi co DUNG MOT don thi ten bi xoa trang: mot don khong phai la "chay theo don",
        /// va de trang thi moi thu phia sau (nhan tren to, bang kiem tra, cach xep hang) im
        /// lang y nhu truoc khi co tinh nang nay - ket qua ghep khong doi mot mili nao.
        /// </summary>
        public string Finalize()
        {
            List<NestingOrderEntry> used = new List<NestingOrderEntry>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Scanned) continue;

                if (string.IsNullOrEmpty(_items[i].Name))
                {
                    return "Dong " + (i + 1).ToString(CultureInfo.InvariantCulture) + " da quet nhung chua co ten don.";
                }

                int duplicate = DuplicateRowOf(i);
                if (duplicate > 0)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "Ten don \"{0}\" bi trung (dong {1} va {2}).", _items[i].Name, i + 1, duplicate);
                }

                used.Add(_items[i]);
            }

            if (used.Count == 0) return "Chua quet chi tiet cho don nao.";

            _items.Clear();
            _items.AddRange(used);
            if (_items.Count == 1) _items[0].Name = string.Empty;
            return null;
        }

        private int IndexOfName(string name, int skip)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (i == skip || string.IsNullOrEmpty(_items[i].Name)) continue;
                if (string.Equals(_items[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }
    }
}
