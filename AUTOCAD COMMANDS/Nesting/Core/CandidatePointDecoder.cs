using System;
using System.Collections.Generic;
using System.Threading;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Bo DAT CHI TIET: sinh cac vi tri ung vien, kiem va cham bang da giac THAT, roi nen lai.
    ///
    /// Voi tung chi tiet (theo thu tu duoc giao) no thu cac to dang mo truoc, het cho thi mo
    /// to moi. Tren mot to, ung vien den tu HAI nguon bo tuc cho nhau:
    ///
    ///   A. QUET NGANG DOC - cac cot X suy tu mep to, tu hop bao cac chi tiet da dat, tu dinh
    ///      lom, cong mot luot quet deu khap chieu dai to. O moi cot: THA ROI tu tren xuong,
    ///      va lay chieu cao hop le thap nhat.
    ///
    ///   B. VI TRI CHAM NHAU (kieu NFP) - ghep tung diem tren duong bao chi tiet DA DAT voi
    ///      tung dinh cua chi tiet DANG XEP, chi giu cac cap quay lung vao nhau, roi lui ra
    ///      theo phap tuyen va ep nguoc vao den khi cham. Xem <see cref="AddContactCandidates"/>.
    ///
    /// Nguon A tim ra phan lon cac cho; nguon B bu dung mot lo ma A khong voi toi: hoc mo sang
    /// NGANG o giua chieu cao chi tiet - tha roi khong toi, ma nen trai/xuong cung khong vao
    /// duoc neu chieu cao khong dung ngay tu dau.
    ///
    /// Vai chuc ung vien dan dau duoc NEN (truot trai roi truot xuong), roi cham diem LAI sau
    /// khi nen - vi truoc khi nen thi hai cho cung cot X trong y het nhau.
    ///
    /// Day KHONG phai NFP dung nghia: khong dung Minkowski, khong hop cac vung cam, khong phu
    /// cac vi tri cham kieu dinh-cham-canh. Do la mot tap UNG VIEN; phep kiem va cham that van
    /// la trong tai duy nhat quyet dinh cho nao dat duoc. Cung khong co GA / SA.
    /// </summary>
    public sealed class CandidatePointDecoder : IPlacementDecoder
    {
        /// <summary>Smallest compaction step (0.05 mm).</summary>
        private const long MinSlideStep = 50;

        /// <summary>First compaction step (25 mm), halved on every blocked move.</summary>
        private const long InitialSlideStep = 25000;

        /// <summary>
        /// Bao nhieu ung vien dan dau duoc dem di NEN (truot trai roi truot xuong).
        ///
        /// Diem cham TRUOC khi nen chi la uoc luong tho: hai cho cung mot cot X cho ra cung
        /// mot diem, nhung sau khi nen thi mot cai truot han vao trong con cai kia bi chan
        /// ngay. Nen it qua thi cai truot duoc lai roi ra ngoai danh sach.
        ///
        /// Da do tren ca bo fixture: 6 -> 8 -> 10 lam quality_arc_in_c_01 tu 758 xuong 730
        /// roi 712 mm; tu 12 tro len khong go them duoc gi ma van ton them thoi gian.
        /// Rieng con so nay ton khoang 1% thoi gian.
        /// </summary>
        private const int CompactedCandidates = 10;

        /// <summary>
        /// So diem X quet deu tren chieu dai to cho moi huong xoay.
        ///
        /// Cac vi tri X doan tu hop bao van duoc giu; day la phan quet THEM cho nhung cho nam
        /// giua chung. 64 diem tren to 2500 mm la buoc ~39 mm - du day de tim ra cho, con lai
        /// buoc nen se don sat den tung 0.05 mm.
        /// </summary>
        private const int XSweepSteps = 64;

        /// <summary>Score quantum for the LeftBottom policy (1 mm) so that tiny compaction noise does not dominate.</summary>
        private const long ScoreQuantum = 1000;

        /// <summary>
        /// So ung vien TIEP XUC duoc dem di kiem va cham that, theo thu tu diem tot dan.
        ///
        /// Sinh ung vien tiep xuc chi la phep tru nen re; kiem va cham moi dat. Vi vay sinh
        /// that nhieu, cham diem bang hop bao (cung re), roi chi kiem that cho tung nay cai
        /// dau bang. Dat 0 la tat han phan NFP - dung de do co/khong tren cung mot may.
        ///
        /// Da do: ha xuong 12 thi mat cho long trong fixture quality_side_v_pocket_01 (685 mm
        /// thay vi 610); 16 la vua du, de 24 cho co bien.
        /// </summary>
        private const int ContactChecked = 24;

        /// <summary>So ung vien tiep xuc HOP LE nhieu nhat duoc giu lai de thi voi cac ung vien quet.</summary>
        private const int ContactKept = 8;

        /// <summary>
        /// Day them ra ngoai bao nhieu, ngoai khe ho bat buoc (0.05 mm - dung bang buoc nen nho
        /// nhat). Diem tiep xuc tinh dung bang khe ho thi cham dung gioi han, lam tron sang so
        /// nguyen la co the thieu mot don vi va bi loai oan.
        /// </summary>
        private const long ContactGuard = 50;

        /// <summary>
        /// Nguong AP MAT: tich vo huong cua hai phap tuyen phai nho hon so nay.
        ///
        /// Hai mep chi ap duoc vao nhau khi chung quay LUNG lai nhau - phap tuyen nguoc chieu,
        /// tich vo huong bang -1. Ghep hai diem co phap tuyen cung chieu thi chi co the la mot
        /// hinh dam xuyen qua hinh kia. Lay -0.35 (lech nhau tren 110 do) de van nhan cac goc,
        /// vi tai mot goc phap tuyen phan giac lech kha xa phap tuyen cua tung canh.
        /// </summary>
        private const double ContactFacing = -0.35;

        /// <summary>
        /// Gop cac vi tri cach nhau duoi 0.5 mm lam mot khi chon ung vien di kiem.
        ///
        /// Nhieu cap diem khac nhau cho ra gan nhu cung mot cho dat; neu khong gop thi ca
        /// ngan sach kiem va cham do het vao mot cho duy nhat. Gop xong, ngan sach trai deu
        /// ra nhieu cho dat KHAC nhau. Vi tri giu nguyen khong lam tron - chi dung de gop.
        /// </summary>
        private const long ContactMergeStep = 500;

        /// <summary>
        /// Giu san bao nhieu ung vien tiep xuc tot nhat.
        ///
        /// Phai lon hon <see cref="ContactChecked"/> kha nhieu, vi buoc gop cho trung sau do se
        /// loai bot: nhieu cap diem khac nhau cho ra gan nhu cung mot cho dat.
        /// </summary>
        private const int ContactPoolKeep = 128;

        /// <summary>
        /// Lui ra bao nhieu theo phuong phap tuyen truoc khi ep vao (20 mm).
        ///
        /// Diem tiep xuc tinh theo duong phan giac chi trung dich khi goc hai ben khop nhau.
        /// Lech mot chut la ung vien nam sau qua, bi coi la khong hop le va bi vut di. Lui ra
        /// truoc roi ep vao thi luon dung o dung cho cham nhau, bat ke goc co khop hay khong.
        /// </summary>
        private const long ContactBackoff = 20000;

        public DecodedLayout Decode(IList<PartInstance> order, MaterialJob job, PlacementPolicy policy, CancellationToken cancellation)
        {
            DecodedLayout layout = new DecodedLayout();
            ICollisionModel collision = job.Collision;

            foreach (PartInstance instance in order)
            {
                if (cancellation.IsCancellationRequested)
                {
                    layout.Cancelled = true;
                    layout.Unplaced.Add(new UnplacedPart(instance.Id, instance.PartGroupId, "Da huy boi nguoi dung.", instance.Order));
                    continue;
                }

                List<PreparedShape> orientations;
                if (!job.Orientations.TryGetValue(instance.PartGroupId, out orientations) || orientations.Count == 0)
                {
                    string reason;
                    job.UnfitReasons.TryGetValue(instance.PartGroupId, out reason);
                    layout.Unplaced.Add(new UnplacedPart(instance.Id, instance.PartGroupId,
                        reason ?? "Khong co huong xoay nao vua kho phoi.", instance.Order));
                    continue;
                }

                bool placed = false;
                foreach (DecodedSheet sheet in layout.Sheets)
                {
                    PlacedItem item = TryPlace(instance, orientations, sheet, job, collision, policy);
                    if (item != null)
                    {
                        AddItem(sheet, item);
                        placed = true;
                        break;
                    }
                }

                if (placed) continue;

                DecodedSheet fresh = new DecodedSheet();
                PlacedItem first = TryPlace(instance, orientations, fresh, job, collision, policy);
                if (first == null)
                {
                    layout.Unplaced.Add(new UnplacedPart(instance.Id, instance.PartGroupId,
                        "Khong tim duoc vi tri hop le ngay ca tren to phoi trong.", instance.Order));
                    continue;
                }

                AddItem(fresh, first);
                layout.Sheets.Add(fresh);
            }

            return layout;
        }

        private static void AddItem(DecodedSheet sheet, PlacedItem item)
        {
            sheet.Items.Add(item);
            if (item.Placed.Bounds.MaxX > sheet.MaxX) sheet.MaxX = item.Placed.Bounds.MaxX;
            if (item.Placed.Bounds.MaxY > sheet.MaxY) sheet.MaxY = item.Placed.Bounds.MaxY;
        }

        private struct Score
        {
            public long K1;
            public long K2;
            public long K3;

            public bool BetterThan(Score o)
            {
                if (K1 != o.K1) return K1 < o.K1;
                if (K2 != o.K2) return K2 < o.K2;
                return K3 < o.K3;
            }
        }

        private static Score MakeScore(PreparedShape s, long tx, long ty, PlacementPolicy policy)
        {
            long minX = s.Bounds.MinX + tx, minY = s.Bounds.MinY + ty;
            long maxX = s.Bounds.MaxX + tx, maxY = s.Bounds.MaxY + ty;
            Score score;
            if (policy == PlacementPolicy.MinLength)
            {
                score.K1 = maxX / ScoreQuantum;
                score.K2 = maxY / ScoreQuantum;
                score.K3 = minX;
            }
            else
            {
                score.K1 = minX / ScoreQuantum;
                score.K2 = minY / ScoreQuantum;
                score.K3 = maxY;
            }

            return score;
        }

        private PlacedItem TryPlace(
            PartInstance instance,
            List<PreparedShape> orientations,
            DecodedSheet sheet,
            MaterialJob job,
            ICollisionModel collision,
            PlacementPolicy policy)
        {
            SheetSpec spec = job.Sheet;
            ClearanceRules rules = collision.Rules;
            long sheetL = spec.LengthUnits, sheetW = spec.WidthUnits;
            PartShape part = instance.Group.Shape;
            long inset = rules.BoundaryInset(part);

            // Pair clearance per placed item (Gap + both approximation tolerances).
            int n = sheet.Items.Count;
            long[] clearance = new long[n];
            for (int i = 0; i < n; i++) clearance[i] = rules.PartClearance(part.ToleranceUnits, sheet.Items[i].Placed.ToleranceUnits);

            // Valid uncompacted positions: the first valid Y for every candidate X and
            // orientation. NO pruning of X candidates (see audit in the class summary).
            List<Candidate> candidates = new List<Candidate>();
            List<PlacedItem> column = new List<PlacedItem>(n);
            List<long> xs = new List<long>();
            List<long> ys = new List<long>();

            foreach (PreparedShape shape in orientations)
            {
                long w = shape.Bounds.Width, h = shape.Bounds.Height;
                long maxXStart = sheetL - inset - w;
                long maxYStart = sheetW - inset - h;
                if (maxXStart < inset || maxYStart < inset) continue;

                // X candidates for the bounding-box left edge: sheet boundary, beside / aligned
                // with placed parts, reflex (concave) corners, and - only when part-in-part is
                // allowed - holes big enough for this part.
                xs.Clear();
                xs.Add(inset);
                xs.Add(maxXStart);
                for (int i = 0; i < n; i++)
                {
                    PlacedItem p = sheet.Items[i];
                    long c = clearance[i];
                    LongRect b = p.Placed.Bounds;
                    xs.Add(b.MaxX + c);
                    xs.Add(b.MinX);
                    xs.Add(b.MinX - c - w);
                    foreach (IntPoint v in p.ReflexVertices)
                    {
                        xs.Add(v.X + c);
                        xs.Add(v.X - c - w);
                    }

                    if (rules.AllowPartInsideHole)
                    {
                        foreach (LongRect hb in p.HoleBounds)
                        {
                            if (HoleCanHold(hb, w, h, c)) xs.Add(hb.MinX + c);
                        }
                    }
                }

                // Quet deu khap chieu dai to: giua hai mep hop bao co the la dung cho vua khit
                // ma khong co diem doan nao roi vao.
                long span = maxXStart - inset;
                if (span > 0)
                {
                    long sweep = Math.Max(ScoreQuantum, span / XSweepSteps);
                    for (long x = inset + sweep; x < maxXStart; x += sweep) xs.Add(x);
                }

                SortUniqueInRange(xs, inset, maxXStart);

                foreach (long x in xs)
                {
                    // Only parts overlapping this column can collide (exact bbox argument).
                    column.Clear();
                    ys.Clear();
                    ys.Add(inset);
                    ys.Add(maxYStart);
                    for (int i = 0; i < n; i++)
                    {
                        PlacedItem p = sheet.Items[i];
                        long c = clearance[i];
                        LongRect b = p.Placed.Bounds;
                        if (!(b.MinX < x + w + c && x < b.MaxX + c)) continue;

                        column.Add(p);
                        ys.Add(b.MaxY + c);
                        ys.Add(b.MinY);
                        ys.Add(b.MinY - c - h);
                        foreach (IntPoint v in p.ReflexVertices)
                        {
                            ys.Add(v.Y + c);
                            ys.Add(v.Y - c - h);
                        }

                        if (rules.AllowPartInsideHole)
                        {
                            foreach (LongRect hb in p.HoleBounds)
                            {
                                if (HoleCanHold(hb, w, h, c)) ys.Add(hb.MinY + c);
                            }
                        }
                    }

                    SortUniqueInRange(ys, inset, maxYStart);

                    // THA ROI: dat chi tiet len tren cung roi ha xuong den khi bi chan. Day moi
                    // la cho no thuc su dung o cot X nay - khac han voi viec chon mot trong vai
                    // gia tri Y doan san tu hop bao.
                    long dropX = x - shape.Bounds.MinX;
                    long dropY = maxYStart - shape.Bounds.MinY;
                    if (IsValid(shape, dropX, dropY, column, spec, collision))
                    {
                        Slide(shape, ref dropX, ref dropY, 0, -1, column, spec, collision);
                        candidates.Add(new Candidate
                        {
                            Shape = shape,
                            Tx = dropX,
                            Ty = dropY,
                            Score = MakeScore(shape, dropX, dropY, policy),
                            Order = candidates.Count
                        });
                    }

                    // Van giu cac ung vien cu: co nhung cho chi vao duoc TU BEN CANH (long chu
                    // C nam ngang, hoc lom mo ngang) ma tha roi thang dung khong bao gio toi.
                    foreach (long y in ys)
                    {
                        long tx = x - shape.Bounds.MinX;
                        long ty = y - shape.Bounds.MinY;
                        if (!IsValid(shape, tx, ty, column, spec, collision)) continue;

                        candidates.Add(new Candidate { Shape = shape, Tx = tx, Ty = ty, Score = MakeScore(shape, tx, ty, policy), Order = candidates.Count });

                        // The lowest valid Y is the one that matters for this X.
                        break;
                    }
                }
            }

            AddContactCandidates(orientations, sheet, spec, collision, inset, clearance, policy, candidates);

            if (candidates.Count == 0) return null;

            candidates.Sort(CompareCandidates);

            Candidate chosen = null;
            for (int i = 0; i < candidates.Count && i < CompactedCandidates; i++)
            {
                Candidate c = candidates[i];
                long tx = c.Tx, ty = c.Ty;
                Compact(c.Shape, ref tx, ref ty, sheet.Items, spec, collision);
                c.Score = MakeScore(c.Shape, tx, ty, policy);

                c.Tx = tx;
                c.Ty = ty;
                if (chosen == null || c.Score.BetterThan(chosen.Score)) chosen = c;
            }

            return new PlacedItem(instance, chosen.Shape, chosen.Tx, chosen.Ty, collision.Place(chosen.Shape, chosen.Tx, chosen.Ty));
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            if (a.Score.BetterThan(b.Score)) return -1;
            if (b.Score.BetterThan(a.Score)) return 1;
            return a.Order.CompareTo(b.Order);
        }

        /// <summary>
        /// Sinh ung vien theo kieu DA GIAC KHONG-VUA (NFP).
        ///
        /// Cach quet cu chi thu cac vi tri doc va ngang suy ra tu hop bao: mot chi tiet chi
        /// duoc thu o nhung cot X va hang Y thang hang voi mep hop bao cua chi tiet da dat.
        /// Vi vay hai hinh cong long vao nhau duoc thi cung khong bao gio duoc thu, tru khi
        /// cho long do tinh co thang hang voi mot mep hop bao nao do.
        ///
        /// Tap hop MOI vi tri dat hai hinh cham nhau chinh la bien cua da giac khong-vua, va
        /// moi dinh cua bien do co dang (mot diem tren hinh da dat) tru (mot dinh cua hinh
        /// dang xep). Sinh thang tap do: voi moi cap diem, tinh tien de hai diem trung nhau,
        /// sau khi da day diem thu nhat ra ngoai dung bang khe ho bat buoc.
        ///
        /// Day KHONG phai NFP dung: khong dung Minkowski, khong hop cac vung cam, va khong
        /// phu cac vi tri cham kieu dinh-cham-canh. Do la mot tap ung vien - phep kiem va cham
        /// that van la trong tai duy nhat quyet dinh cho nao dat duoc, va buoc nen van don sat
        /// den tung 0.05 mm. Khong noi long dung sai o bat cu dau.
        /// </summary>
        private static void AddContactCandidates(
            List<PreparedShape> orientations,
            DecodedSheet sheet,
            SheetSpec spec,
            ICollisionModel collision,
            long inset,
            long[] clearance,
            PlacementPolicy policy,
            List<Candidate> candidates)
        {
            int n = sheet.Items.Count;
            if (n == 0 || ContactChecked <= 0) return;

            // Cac ung vien quet da co san mot cho tot nhat. Cho cham nao khong hon duoc cho do
            // thi sinh ra cung vo ich - bo ngay truoc khi cap phat, vi so cap diem la hang chuc
            // nghin va chinh viec cap phat moi la phan ton thoi gian.
            Score limit;
            limit.K1 = long.MaxValue;
            limit.K2 = long.MaxValue;
            limit.K3 = long.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Score.BetterThan(limit)) limit = candidates[i].Score;
            }

            long sheetL = spec.LengthUnits, sheetW = spec.WidthUnits;
            Candidate[] pool = new Candidate[ContactPoolKeep];
            int poolCount = 0, generated = 0;

            foreach (PreparedShape shape in orientations)
            {
                IntPoint[] moving = shape.ContactPoints;
                if (moving.Length == 0) continue;

                LongRect box = shape.Bounds;
                if (box.Width > sheetL - 2 * inset || box.Height > sheetW - 2 * inset) continue;

                double[] mnx = shape.ContactNormalX, mny = shape.ContactNormalY;

                for (int i = 0; i < n; i++)
                {
                    PlacedItem p = sheet.Items[i];
                    long gap = clearance[i] + ContactGuard;

                    IntPoint[] fixedPts = p.ContactPoints;
                    double[] fnx = p.ContactNormalX, fny = p.ContactNormalY, fpush = p.ContactPush;

                    for (int a = 0; a < fixedPts.Length; a++)
                    {
                        double nax = fnx[a], nay = fny[a];
                        long ax = fixedPts[a].X + (long)Math.Round(nax * fpush[a] * gap);
                        long ay = fixedPts[a].Y + (long)Math.Round(nay * fpush[a] * gap);

                        for (int b = 0; b < moving.Length; b++)
                        {
                            // Chi giu cac cap AP MAT vao nhau; con lai la dam xuyen qua nhau.
                            if (nax * mnx[b] + nay * mny[b] > ContactFacing) continue;

                            long tx = ax - moving[b].X;
                            long ty = ay - moving[b].Y;

                            // Loai som bang hop bao - re hon phep kiem va cham hang nghin lan.
                            if (box.MinX + tx < inset || box.MinY + ty < inset) continue;
                            if (box.MaxX + tx > sheetL - inset || box.MaxY + ty > sheetW - inset) continue;

                            Score score = MakeScore(shape, tx, ty, policy);
                            if (!score.BetterThan(limit)) continue;

                            // Day la cho nong nhat cua ca ham: chay hang chuc nghin lan moi
                            // lan dat mot chi tiet. Khong hon duoc cai te nhat dang giu thi bo
                            // ngay, truoc khi cap phat bat cu thu gi.
                            if (poolCount == ContactPoolKeep && !score.BetterThan(pool[poolCount - 1].Score)) 
                            {
                                generated++;
                                continue;
                            }

                            Candidate made = new Candidate
                            {
                                Shape = shape,
                                Tx = tx,
                                Ty = ty,
                                Score = score,
                                Order = generated++,
                                Nx = nax,
                                Ny = nay
                            };

                            // Chen giu thu tu. Hoa diem thi cai sinh TRUOC o lai, dung nhu mot
                            // phep sap xep on dinh se lam.
                            int at = poolCount < ContactPoolKeep ? poolCount++ : ContactPoolKeep - 1;
                            while (at > 0 && made.Score.BetterThan(pool[at - 1].Score))
                            {
                                pool[at] = pool[at - 1];
                                at--;
                            }

                            pool[at] = made;
                        }
                    }
                }
            }

            if (poolCount == 0) return;

            HashSet<long> seen = new HashSet<long>();
            int kept = 0, checks = 0;
            for (int i = 0; i < poolCount && checks < ContactChecked && kept < ContactKept; i++)
            {
                Candidate c = pool[i];

                // Gop cac cho dat sat nhau: ngan sach kiem va cham phai trai ra nhieu cho
                // KHAC nhau, chu khong do het vao mot chum diem gan nhu trung.
                long cell = (c.Tx / ContactMergeStep + 4000000L) * 10000000L
                          + (c.Ty / ContactMergeStep + 4000000L);
                if (!seen.Add(cell)) continue;

                checks++;

                // Lui ra doc theo phap tuyen roi ep nguoc vao den khi cham. Dat thang vao diem
                // tinh san chi trung khi goc hai ben khop nhau; lui ra truoc thi luon dung o
                // dung cho cham, va cho nao that su bi chan thi cung thay ngay o buoc lui.
                long backX = c.Tx + (long)Math.Round(c.Nx * ContactBackoff);
                long backY = c.Ty + (long)Math.Round(c.Ny * ContactBackoff);
                if (!IsValid(c.Shape, backX, backY, sheet.Items, spec, collision)) continue;

                // Ep vao QUA ca diem tinh san: neu chi tiet HEP hon hoc thi cho cham that nam
                // sau hon diem do. Chi nhan cac vi tri hop le nen ep sau chi co the sat hon.
                SlideAlong(c.Shape, ref backX, ref backY, -c.Nx, -c.Ny, 2 * ContactBackoff, sheet.Items, spec, collision);
                c.Tx = backX;
                c.Ty = backY;
                c.Score = MakeScore(c.Shape, backX, backY, policy);

                // Xep sau cac ung vien quet: hoa diem thi cach cu thang, nen chi them lua
                // chon chu khong lam doi ket qua da co.
                c.Order = candidates.Count;
                candidates.Add(c);
                kept++;
            }
        }

        private sealed class Candidate
        {
            public PreparedShape Shape;
            public long Tx;
            public long Ty;
            public Score Score;
            public int Order;

            /// <summary>Phuong phap tuyen tai cho cham: lui ra theo huong nay roi ep nguoc vao.</summary>
            public double Nx;

            public double Ny;
        }

        /// <summary>
        /// Truot theo mot phuong BAT KY (khong chi ngang hoac doc) cho den khi khong di tiep
        /// duoc nua. Buoc dau bang <paramref name="room"/>, moi lan bi chan thi chia doi, dung
        /// lai o 0.05 mm - giong het cach <see cref="Slide"/> lam voi hai truc.
        /// </summary>
        private static void SlideAlong(
            PreparedShape shape,
            ref long tx,
            ref long ty,
            double ux,
            double uy,
            long room,
            List<PlacedItem> items,
            SheetSpec spec,
            ICollisionModel collision)
        {
            long x0 = tx, y0 = ty;
            long moved = 0;
            long step = room;
            while (step >= MinSlideStep)
            {
                long d = moved + step;
                if (d <= room)
                {
                    long nx = x0 + (long)Math.Round(ux * d);
                    long ny = y0 + (long)Math.Round(uy * d);
                    if (IsValid(shape, nx, ny, items, spec, collision))
                    {
                        moved = d;
                        continue;
                    }
                }

                step /= 2;
            }

            tx = x0 + (long)Math.Round(ux * moved);
            ty = y0 + (long)Math.Round(uy * moved);
        }

        private static bool HoleCanHold(LongRect hole, long w, long h, long clearance)
        {
            return hole.Width >= w + 2 * clearance && hole.Height >= h + 2 * clearance;
        }

        private static void SortUniqueInRange(List<long> values, long min, long max)
        {
            values.RemoveAll(v => v < min || v > max);
            values.Sort();
            int k = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (k == 0 || values[i] != values[k - 1]) values[k++] = values[i];
            }

            values.RemoveRange(k, values.Count - k);
        }

        private static bool IsValid(PreparedShape shape, long tx, long ty, List<PlacedItem> items, SheetSpec spec, ICollisionModel collision)
        {
            if (!collision.FitsInsideSheet(shape, tx, ty, spec)) return false;
            foreach (PlacedItem p in items)
            {
                if (collision.Collides(shape, tx, ty, p.Placed)) return false;
            }

            return true;
        }

        /// <summary>Slides left then down (repeatedly) as long as the position stays valid.</summary>
        private static void Compact(PreparedShape shape, ref long tx, ref long ty, List<PlacedItem> items, SheetSpec spec, ICollisionModel collision)
        {
            for (int round = 0; round < 6; round++)
            {
                long movedX = Slide(shape, ref tx, ref ty, -1, 0, items, spec, collision);
                long movedY = Slide(shape, ref tx, ref ty, 0, -1, items, spec, collision);
                if (movedX < MinSlideStep && movedY < MinSlideStep) break;
            }
        }

        private static long Slide(PreparedShape shape, ref long tx, ref long ty, int dx, int dy, List<PlacedItem> items, SheetSpec spec, ICollisionModel collision)
        {
            long inset = collision.Rules.BoundaryInset(shape.Group.Shape);
            long room = dx != 0 ? shape.Bounds.MinX + tx - inset : shape.Bounds.MinY + ty - inset;
            if (room <= 0) return 0;

            // Straight to the boundary when nothing is in the way.
            if (IsValid(shape, tx + dx * room, ty + dy * room, items, spec, collision))
            {
                tx += dx * room;
                ty += dy * room;
                return room;
            }

            long moved = 0;
            long step = Math.Min(room, InitialSlideStep);
            while (step >= MinSlideStep)
            {
                if (moved + step <= room &&
                    IsValid(shape, tx + dx * (moved + step), ty + dy * (moved + step), items, spec, collision))
                {
                    moved += step;
                }
                else
                {
                    step /= 2;
                }
            }

            tx += dx * moved;
            ty += dy * moved;
            return moved;
        }
    }
}
