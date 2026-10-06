using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;
using static AUTOCAD_COMMANDS.Nesting.SelfTests.NestingTestHarness;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    /// <summary>Tests for contour building, SL/material parsing and text association (no AutoCAD).</summary>
    public static class RecognitionSelfTests
    {
        public static void Run(NestingTestReport report)
        {
            NestingTestHarness.Run(report, "R01. Doc SL nhieu dinh dang", R01_QuantityFormats);
            NestingTestHarness.Run(report, "R02. Doc vat lieu nhieu dinh dang", R02_MaterialFormats);
            NestingTestHarness.Run(report, "R03. Khong qua de dai (150MM, ASL 3, SL 0)", R03_NotTooPermissive);
            NestingTestHarness.Run(report, "R04. Duong bao tu 4 LINE roi -> 1 chi tiet", R04_LinesFormOnePart);
            NestingTestHarness.Run(report, "R05. Lo ben trong + chi tiet nam trong lo", R05_HoleAndPartInHole);
            NestingTestHarness.Run(report, "R06. Duong bao ho -> VAN LA 1 PHOI (tu noi khe ho, van ghep)", R06_OpenContourInvalid);
            NestingTestHarness.Run(report, "R07. Duong chan ben trong -> marking, khong loi", R07_BendLineInsideIsMarking);
            NestingTestHarness.Run(report, "R08. Duong bao tu cat -> VAN GHEP (bao loi)", R08_SelfIntersectionInvalid);
            NestingTestHarness.Run(report, "R09. Text trong chi tiet duoc gan", R09_TextInside);
            NestingTestHarness.Run(report, "R10. Text NGOAI chi tiet -> gan chi tiet gan nhat", R10_TextOutsideNearest);
            NestingTestHarness.Run(report, "R11. Text o giua 2 chi tiet -> AMBIGUOUS", R11_TextAmbiguous);
            NestingTestHarness.Run(report, "R12. Thieu SL -> 1, thieu vat lieu -> 1.2MM", R12_Defaults);
            NestingTestHarness.Run(report, "R13. Hai SL khac nhau cho 1 chi tiet -> AMBIGUOUS", R13_ConflictingQuantities);
            NestingTestHarness.Run(report, "R14. Text qua xa -> canh bao chung", R14_TextTooFar);
            NestingTestHarness.Run(report, "R15. Hinh re nhanh -> GOP theo duong bao ngoai cung", R15_BranchingInvalid);
            NestingTestHarness.Run(report, "R16. Duong trung nhau (dien tich 0) -> VAN GHEP (chu nhat bao)", R16_ZeroArea);
            NestingTestHarness.Run(report, "R17. Ban ghi mo ho chua xac nhan bi chan", R17_UnconfirmedAmbiguousBlocked);
            NestingTestHarness.Run(report, "R17b. Chu giua hai chi tiet: ben da co SL thi gan cho ben chua co", R17b_BetweenTwoPartsResolved);
            NestingTestHarness.Run(report, "R18. 1 block chua 2 chi tiet -> GOP thanh 1 phoi (khong nhan doi)", R18_SharedSourceInvalid);
            NestingTestHarness.Run(report, "R19. Hinh tren layer danh dau gan vao chi tiet chua no", R19_AttachMarking);
            NestingTestHarness.Run(report, "R20. SL trong + vat lieu ngoai (2 vi tri khac nhau)", R20_SlInsideMaterialOutside);
            NestingTestHarness.Run(report, "R21. Vat lieu ngoai, SL ngoai o 2 phia", R21_BothOutsideDifferentSides);
            NestingTestHarness.Run(report, "R22. Nguong khoang cach: 300 gan, 300.01 khong", R22_DistanceThresholdExact);
            NestingTestHarness.Run(report, "R23. Do day ngoai khoang 0.3-25 bi loai", R23_ThicknessRange);
            NestingTestHarness.Run(report, "R24. Chi tiet toan doan thang -> dung sai 0; co cung -> dung sai cung", R24_ToleranceFlag);
            NestingTestHarness.Run(report, "R25. Text gan 2 chi tiet khac nhau ro -> gan dung, khong mo ho", R25_ClearlyCloserNotAmbiguous);
            NestingTestHarness.Run(report, "R26. Hai duong bao chong nhau -> GOP, ghi chu kem TOA DO", R26_TouchingContoursReportWhere);
            NestingTestHarness.Run(report, "R27. Hai duong bao TRUNG KHIT -> GOP lam 1, khong bao loi", R27_DuplicateContourIsMerged);
            NestingTestHarness.Run(report, "R26b. Lo cham bien / hai chi tiet chong nhau -> GOP, giu lo con lai", R26b_TouchingMergeKeepsFreeHoles);
            NestingTestHarness.Run(report, "R32. Hinh ho: gan kin thi giu, net thua thi bo", R32_OpenGeometryKeptOnlyIfAlmostClosed);
            NestingTestHarness.Run(report, "R33. Chu CAT trong phoi di theo phoi; chu THONG TIN thi khong", R33_InsideCutTextTravelsWithPart);
            NestingTestHarness.Run(report, "R28. Chu khac trong chi tiet -> gan de XUAT, khong phai marking", R28_EngravingInsideAttaches);
            NestingTestHarness.Run(report, "R29. Chu khac ngoai moi chi tiet -> khong gan", R29_EngravingOutsideNotAttached);
            NestingTestHarness.Run(report, "R30. Chu khac o chi tiet long nhau -> chi tiet TRONG CUNG", R30_EngravingInnermostWins);
            NestingTestHarness.Run(report, "R31. Nhieu chu khac / nhieu chi tiet -> khong lan nhau", R31_EngravingManyPartsSeparate);
            NestingTestHarness.Run(report, "R34. SL: 0010 / 2 cai / 3 (bo) / 99999", R34_QuantityVariants);
            NestingTestHarness.Run(report, "R35. SL / vat lieu lap lai GIONG nhau -> khong mo ho", R35_DuplicateMetadataNotAmbiguous);
            NestingTestHarness.Run(report, "R36. Hai vat lieu khac nhau -> AMBIGUOUS, chua xac nhan thi bi chan", R36_ConflictingMaterials);
            NestingTestHarness.Run(report, "R37. 'SL: 2 SL: 3' trong 1 chu -> AMBIGUOUS", R37_TwoQuantitiesOneText);
            NestingTestHarness.Run(report, "R38. Nguong mo ho: chenh 0.4 mm -> AMBIGUOUS, 0.6 mm -> chi tiet gan hon", R38_AmbiguityBoundary);
            NestingTestHarness.Run(report, "R39. SL rieng ben trong + SL chi tiet ben canh gan vien -> bao, khong im lang", R39_NeighbourQuantityFlagged);
            NestingTestHarness.Run(report, "R40. Phan loai SL: hop le / SAI / khong phai SL; dau phay sau SL", R40_QuantityClassification);
            NestingTestHarness.Run(report, "R41. Vat lieu / kich thuoc (R, D, O, ngoai khoang) / so AM", R41_MaterialVersusDimension);
            NestingTestHarness.Run(report, "R42. SL sai / vat lieu am -> MO HO co ghi chu, khong thanh chu cat; kich thuoc bi bo qua", R42_InvalidMetadataRecognition);
            NestingTestHarness.Run(report, "R43. Chu trong LO cua khung: 10 truong hop topo", R43_TextInsideHoleTopology);
            NestingTestHarness.Run(report, "R44. Toa do rat xa (+-1e6 .. 1e7 mm): cung ket qua nhan dang", R44_FarCoordinates);
            NestingTestHarness.Run(report, "R45. Doi khang: dau hai cham full-width, 0MM, duoi MM la, sat bien 0.001, chu chong cho", R45_AdversarialMetadata);
            NestingTestHarness.Run(report, "R46. MText 3 dong 'SL: 1 / 0.75MM / CHAN DOI XUNG' -> SL + do day + ten", R46_MultiLineMTextNameAndMaterial);
            NestingTestHarness.Run(report, "R47. Loai vat lieu: INOX / SUS304 / THEP / MA KEM; INOX va THEP cung do day KHONG tron", R47_MaterialTypes);
            NestingTestHarness.Run(report, "R48. Ten vat lieu: tach / ghep / danh muc kho ghi do day, loai hoac ca hai", R48_MaterialNameMatching);
            NestingTestHarness.Run(report, "R49. Phoi GIONG HET phoi khac ma khong co chu -> MO HO (ban sao / doi xung)", R49_UntextedTwinFlagged);
            NestingTestHarness.Run(report, "R50. Cach ghi o xuong: TON DEN / TON LANH / THEP KHONG GI / 1.2 LY / 1 LY 2 / T=1.2 / TON 1.2", R50_WorkshopWritings);
            NestingTestHarness.Run(report, "R51. Bang QUY DOI loai cua xuong (TON=THEP, TOLE=THEP)", R51_TypeAliases);
        }

        private static void R46_MultiLineMTextNameAndMaterial()
        {
            foreach (string mtext in new[] { "SL: 1\r\n0.75MM\r\nCHAN DOI XUNG", "SL: 1\n0.75MM\nCHAN DOI XUNG", "SL: 1 0.75MM CHAN DOI XUNG" })
            {
                RecognizedPart p = Valid(Recognize(new List<CurveChain> { Box(0, 0, 300, 200) }, Text(mtext, 150, 100)))[0];
                Equal(1, p.Quantity, "SL");
                Equal("0.75MM", p.Material, "do day dong 2 phai duoc doc");
                True(p.QuantityFromText && p.MaterialFromText, "ca hai tu chu");
                Equal(PartStatus.Ok, p.Status, "du thong tin -> OK");
                True(p.Name.EndsWith("CHAN DOI XUNG", StringComparison.Ordinal), "phan chu con lai lam ten: " + p.Name);
                Equal(0, p.EngravingSources.Count, "chu thong tin KHONG bi mang di cat");
            }

            // Chu don vi / chu dem khong thanh ten.
            RecognizedPart q = Valid(Recognize(new List<CurveChain> { Box(0, 0, 300, 200) }, Text("SL: 2 cai\n1.2MM", 150, 100)))[0];
            Equal("P1", q.Name, "'cai' khong phai ten");
        }

        private static void R47_MaterialTypes()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            string[] texts = { "INOX 1.2MM", "SUS304 1.5MM", "SUS 304", "INOX-201", "TH\u00c9P 2MM", "thep 2mm", "TON MA KEM 0.8MM", "SS400", "SS304", "NHOM 3MM", "TON" };
            string[] want = { "INOX", "INOX 304", "INOX 304", "INOX 201", "THEP", "THEP", "MA KEM", "THEP", "INOX 304", "NHOM", "TON" };
            for (int i = 0; i < texts.Length; i++)
            {
                MetadataFact f = p.Parse(texts[i]).Find(x => x.Kind == MetadataKind.MaterialType);
                True(f != null && f.Value == want[i], "'" + texts[i] + "' -> " + want[i] + " (doc duoc: " + (f == null ? "-" : f.Value) + ")");
            }

            foreach (string none in new[] { "CHAN DOI XUNG", "DONG GOI", "SLOT 3", "AL", "TONG", "STONE" })
            {
                True(!p.Parse(none).Exists(x => x.Kind == MetadataKind.MaterialType), "'" + none + "' khong phai loai vat lieu");
            }

            Equal("INOX 304", p.ParseType("sus304"), "o sua tay");
            True(p.ParseType("abc") == null, "khong nhan ra -> null");

            // Nhan dang: loai ghi tren ban ve thang; khong ghi thi lay loai mac dinh.
            RecognitionSettings rs = new RecognitionSettings();
            rs.Metadata.DefaultMaterialType = "THEP";
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(600, 0, 200, 100) };
            RecognitionResult r = new PartRecognizer(rs).Recognize(c, new[] { Text("SL: 2\nINOX 1.2MM", 100, 50), Text("SL: 3\n1.2MM", 700, 50) });
            RecognizedPart a = At(r, 0), b = At(r, 600);
            Equal("INOX 1.2MM", a.Material, "INOX tu chu");
            True(a.MaterialTypeFromText, "co co loai tu chu");
            Equal("THEP 1.2MM", b.Material, "khong ghi loai -> mac dinh THEP");
            True(!b.MaterialTypeFromText, "loai mac dinh");
            Equal(PartStatus.Ok, a.Status, "A OK");
            Equal(PartStatus.Ok, b.Status, "B OK (khong ghi loai khong phai loi)");

            foreach (RecognizedPart x in r.Parts) x.Include = true;
            List<Core.PartGroup> groups = PartRecognizer.ToPartGroups(r.Parts, 0.05);
            True(groups[0].Material != groups[1].Material, "INOX 1.2MM va THEP 1.2MM la hai vat lieu - khong ghep chung");

            // "INOX" + "INOX 304" = cu the hon, khong mau thuan; "INOX" + "THEP" = mau thuan.
            RecognizedPart same = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("INOX 1.2MM", 100, 50), Text("SUS304", 100, 30)))[0];
            Equal("INOX 304 1.2MM", same.Material, "mac cu the thang");
            True(same.Status != PartStatus.Ambiguous, "khong mo ho");
            RecognizedPart clash = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("INOX 1.2MM", 100, 50), Text("THEP", 100, 30)))[0];
            Equal(PartStatus.Ambiguous, clash.Status, "hai loai khac nhau -> mo ho");

            // Mac dinh rong (nhu truoc): khong co loai -> chi do day.
            Equal("1.2MM", Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 1 1.2MM", 100, 50)))[0].Material, "khong loai = nhu cu");
        }

        private static void R48_MaterialNameMatching()
        {
            string type, thick;
            Core.MaterialName.Split("INOX 304 1.2MM", out type, out thick);
            Equal("INOX 304", type, "tach loai");
            Equal("1.2MM", thick, "tach do day");
            Core.MaterialName.Split("1.2MM", out type, out thick);
            True(type.Length == 0 && thick == "1.2MM", "chi do day");
            Equal("THEP 2MM", Core.MaterialName.Compose("thep", "2mm"), "ghep");
            Equal("INOX 2MM", Core.MaterialName.WithType("THEP 2MM", "INOX"), "doi loai giu do day");

            True(Core.MaterialName.EntryMatches("1.2MM", "THEP 1.2MM"), "danh muc cu ghi do day -> moi loai");
            True(Core.MaterialName.EntryMatches("INOX", "INOX 304 1.2MM"), "ghi loai -> moi mac / do day");
            True(Core.MaterialName.EntryMatches("INOX 1.2MM", "INOX 1.2MM"), "day du");
            True(!Core.MaterialName.EntryMatches("INOX 304", "INOX 1.2MM"), "ghi mac -> chi mac do");
            True(!Core.MaterialName.EntryMatches("INOX 1.2MM", "THEP 1.2MM"), "khac loai");
            True(!Core.MaterialName.EntryMatches("1.5MM", "INOX 1.2MM"), "khac do day");
            True(!Core.MaterialName.EntryMatches("INOX", "1.2MM"), "vat lieu khong loai khong khop muc chi loai");

            Core.SheetSpec sheet = new Core.SheetSpec("K", 2440, 1220);
            sheet.Materials.Add("INOX");
            True(sheet.IsCompatibleWith("INOX 1.5MM") && !sheet.IsCompatibleWith("THEP 1.5MM"), "kho chi danh cho INOX");

            List<string> sorted = new List<string> { "THEP 10MM", "INOX 2MM", "THEP 2MM", "INOX 1.2MM" };
            sorted.Sort(Core.MaterialName.Compare);
            Equal("INOX 1.2MM|INOX 2MM|THEP 2MM|THEP 10MM", string.Join("|", sorted.ToArray()), "sap theo loai roi do day (so)");
        }

        private static void R50_WorkshopWritings()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            string[,] cases =
            {
                // chu tren ban ve          loai           do day
                { "TÔN 1.2MM",              "TON",         "1.2MM" },
                { "TÔN ĐEN 1.5MM",          "THEP",        "1.5MM" },
                { "TÔN LẠNH 0.8MM",         "TON LANH",    "0.8MM" },
                { "TÔN MẠ KẼM 1MM",         "MA KEM",      "1MM" },
                { "THÉP KHÔNG GỈ 1.2MM",    "INOX",        "1.2MM" },
                { "THÉP TẤM 3MM",           "THEP",        "3MM" },
                { "INOX 1.2 LY",            "INOX",        "1.2MM" },
                { "TÔN 1 LY 2",             "TON",         "1.2MM" },
                { "1,5 ly",                 "",            "1.5MM" },
                { "TÔN 1.2",                "TON",         "1.2MM" },
                { "SUS304 1.2",             "INOX 304",    "1.2MM" },
                { "INOX T=1.2",             "INOX",        "1.2MM" },
                { "1.2T",                   "",            "1.2MM" },
                { "δ1.2",                   "",            "1.2MM" },
                { "dày 1.2",                "",            "1.2MM" },
                { "GALV 1.2MM",             "MA KEM",      "1.2MM" },
                { "TOLE 1.2MM",             "TON",         "1.2MM" },
                { "SL: 3 INOX 2",           "INOX",        "2MM" },
                { "INOX 304",               "INOX 304",    "" },
                { "TON-01",                 "",            "" }
            };

            for (int i = 0; i < cases.GetLength(0); i++)
            {
                List<MetadataFact> f = p.Parse(cases[i, 0]);
                MetadataFact type = f.Find(x => x.Kind == MetadataKind.MaterialType);
                MetadataFact thick = f.Find(x => x.Kind == MetadataKind.Material);
                Equal(cases[i, 1], type == null ? string.Empty : type.Value, "'" + cases[i, 0] + "' loai");
                Equal(cases[i, 2], thick == null ? string.Empty : thick.Value, "'" + cases[i, 0] + "' do day");
            }

            // Khong duoc doc nham: kich thuoc, SL, ma chi tiet.
            Equal(0, p.Parse("R12.5").Count, "R12.5 khong phai do day");
            Equal(0, p.Parse("150MM").Count, "150MM la kich thuoc");
            Equal(1, p.Parse("SL: 2 cai").Count, "chi co SL");
            Equal(3, p.Parse("SL: 2 T=1.2 INOX").Count, "SL + do day + loai");
        }

        private static void R51_TypeAliases()
        {
            MetadataRules rules = new MetadataRules { MaterialTypeAliases = new List<string> { "tôn=thép", "TOLE = THEP", "sai dong", "=X" } };
            MetadataParser p = new MetadataParser(rules);
            Equal("THEP", p.ParseType("TÔN"), "TON quy doi thanh THEP");
            Equal("THEP", p.ParseType("tole"), "chu moi TOLE -> THEP");
            Equal("MA KEM", p.ParseType("TON KEM"), "TON KEM van la MA KEM: cum dai hon luon thang");
            Equal("THEP", p.MapType("TON"), "doi ten loai da nhan ra");
            Equal("INOX", p.MapType("INOX"), "loai khong quy doi giu nguyen");
            Equal(2, MetadataParser.ParseAliases(rules.MaterialTypeAliases).Count, "bo dong sai");

            // Nhan dang: TON 1.2MM va THEP 1.2MM thanh CUNG vat lieu.
            RecognitionSettings rs = new RecognitionSettings();
            rs.Metadata.DefaultMaterialType = "THEP";
            rs.Metadata.MaterialTypeAliases = new List<string> { "TON=THEP" };
            RecognitionResult r = new PartRecognizer(rs).Recognize(
                new List<CurveChain> { Box(0, 0, 200, 100), Box(600, 0, 200, 100) },
                new[] { Text("SL: 1\nTÔN 1.2MM", 100, 50), Text("SL: 1\n1.2MM", 700, 50) });
            Equal(At(r, 0).Material, At(r, 600).Material, "TON quy doi = THEP mac dinh -> ghep chung");
        }

        private static void R49_UntextedTwinFlagged()
        {
            // A co chu, B y het (ban sao) khong co chu, C khac hinh khong co chu, D = A xoay 90 do.
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 200, 100), Box(1000, 0, 200, 100), Box(2000, 0, 150, 150), Box(3000, 0, 100, 200)
            };
            RecognitionResult r = Recognize(c, Text("SL: 1\n0.75MM", 100, 50));
            Equal(2, PartRecognizer.FlagUntextedTwins(r.Parts), "B va D (xoay) bi danh dau");
            Equal(PartStatus.Ok, At(r, 0).Status, "A giu nguyen");
            Equal(PartStatus.Ambiguous, At(r, 1000).Status, "ban sao khong chu -> mo ho");
            True(At(r, 1000).NotesText.Contains("BAN SAO"), "ghi chu noi ro");
            Equal(PartStatus.Ambiguous, At(r, 3000).Status, "xoay 90 do van la cung hinh");
            True(At(r, 2000).Status != PartStatus.Ambiguous, "khac hinh -> khong dung vao");

            // Moi chi tiet deu co chu rieng -> khong danh dau gi.
            RecognitionResult ok = Recognize(new List<CurveChain> { Box(0, 0, 200, 100), Box(1000, 0, 200, 100) },
                Text("SL: 1\n0.75MM", 100, 50), Text("SL: 2\n1.2MM", 1100, 50));
            Equal(0, PartRecognizer.FlagUntextedTwins(ok.Parts), "du chu -> khong co gi");

            // Khong chi tiet nao co chu -> khong co can cu de so sanh, giu nhu cu.
            RecognitionResult bare = Recognize(new List<CurveChain> { Box(0, 0, 200, 100), Box(1000, 0, 200, 100) });
            Equal(0, PartRecognizer.FlagUntextedTwins(bare.Parts), "khong chu nao -> khong danh dau");
        }

        private static void R20_SlInsideMaterialOutside()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100), Box(600, 0, 200, 100) },
                Text("SL: 12", 100, 50), Text("1.5MM", 100, -25));
            RecognizedPart a = Valid(r).Find(p => p.MinX < 1), b = Valid(r).Find(p => p.MinX > 1);
            Equal(12, a.Quantity, "SL inside");
            Equal("1.5MM", a.Material, "material outside below");
            Equal(PartStatus.Ok, a.Status, "A ok");
            Equal("1.2MM", b.Material, "B untouched -> default");
            Equal(PartStatus.Warning, b.Status, "B defaults visible");
        }

        private static void R21_BothOutsideDifferentSides()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100) },
                Text("SL:4", 100, 130), Text("1,2 MM", 250, 50));
            RecognizedPart a = Valid(r)[0];
            Equal(4, a.Quantity, "SL above");
            Equal("1.2MM", a.Material, "material to the right");
            True(a.QuantityFromText && a.MaterialFromText, "both from text");
        }

        private static void R22_DistanceThresholdExact()
        {
            RecognitionResult near = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 3", 400, 50));   // exactly 300
            Equal(3, Valid(near)[0].Quantity, "at the threshold -> assigned");
            RecognitionResult far = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 3", 400.01, 50));
            Equal(1, Valid(far)[0].Quantity, "beyond -> not assigned");
            Equal(1, far.GlobalWarnings.Count, "and reported");
        }

        private static void R23_ThicknessRange()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            Equal(0, p.Parse("0.1MM").Count, "0.1 below range");
            Equal(0, p.Parse("30MM").Count, "30 above range");
            Equal("0.3MM", p.Parse("0.3MM")[0].Value, "lower bound inclusive");
            Equal("25MM", p.Parse("25MM")[0].Value, "upper bound inclusive");
        }

        private static void R24_ToleranceFlag()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 100, 100),
                new CurveChain(_src++, new List<Pt> { new Pt(300, 0), new Pt(400, 0), new Pt(400, 100), new Pt(300, 100) }, true, true)
            };
            RecognitionResult r = Recognize(c);
            foreach (RecognizedPart p in r.Parts) p.Include = true;
            List<Core.PartGroup> groups = PartRecognizer.ToPartGroups(r.Parts, 0.05);
            Core.PartGroup exact = groups.Find(g => ((RecognizedPart)g.SourceReference).MinX < 1);
            Core.PartGroup arc = groups.Find(g => ((RecognizedPart)g.SourceReference).MinX > 1);
            Close(0.0, exact.Shape.ToleranceMm, 1e-12, "line-only part is an exact polygon");
            Close(0.05, arc.Shape.ToleranceMm, 1e-12, "curve part carries the chord tolerance");
        }

        private static void R25_ClearlyCloserNotAmbiguous()
        {
            // 10 mm from A, 40 mm from B: clearly A.
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(250, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 9", 210, 50));
            RecognizedPart a = Valid(r).Find(p => p.MinX < 1), b = Valid(r).Find(p => p.MinX > 1);
            Equal(9, a.Quantity, "nearest");
            True(a.Status != PartStatus.Ambiguous && b.Status != PartStatus.Ambiguous, "not ambiguous");
            Equal(1, b.Quantity, "B untouched");
        }

        private static void R18_SharedSourceInvalid()
        {
            // Two closed loops coming from the same source entity (e.g. one exploded block).
            List<CurveChain> c = new List<CurveChain>
            {
                new CurveChain(900, new List<Pt> { new Pt(0, 0), new Pt(100, 0), new Pt(100, 100), new Pt(0, 100) }, true),
                new CurveChain(900, new List<Pt> { new Pt(300, 0), new Pt(400, 0), new Pt(400, 100), new Pt(300, 100) }, true)
            };
            RecognitionResult r = Recognize(c);

            // Khong tach duoc khi xuat (sao chep block hai lan = nhan doi hinh) -> GOP thanh 1 phoi.
            Equal(1, r.Parts.Count, "gop thanh mot phoi");
            True(r.Parts[0].IsNestable && r.Parts[0].Include, "van duoc ghep");
            True(r.Parts[0].NotesText.Contains("DA GOP"), "noi ro: " + r.Parts[0].NotesText);
            AssertCoversChains(r.Parts[0], c);
            AssertNestsValid(r);
        }

        private static void R19_AttachMarking()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100), Box(500, 0, 50, 50) });
            True(PartRecognizer.AttachMarking(r.Parts, 777, new List<Pt> { new Pt(60, 0), new Pt(60, 100) }), "inside first part");
            True(!PartRecognizer.AttachMarking(r.Parts, 778, new List<Pt> { new Pt(250, 0), new Pt(260, 10) }), "outside every part");
            RecognizedPart big = Valid(r).Find(p => p.Outer.Area > 10000);
            True(big.MarkingSources.Contains(777) && big.GeometrySources.Contains(777), "attached as marking + output source");
        }

        private static int _src;

        private static CurveChain Chain(bool closed, params double[] xy)
        {
            List<Pt> pts = new List<Pt>();
            for (int i = 0; i + 1 < xy.Length; i += 2) pts.Add(new Pt(xy[i], xy[i + 1]));
            return new CurveChain(_src++, pts, closed);
        }

        /// <summary>
        /// HOI QUY (ban ve that cua nguoi dung, GHOPPHOI TEST-1.dxf):
        /// mot chi tiet dai 2446 mm co 27 lo bi bao "duong bao cat/cham duong bao khac" ma
        /// KHONG noi cham o dau - khong the tim ra cho sai de sua ban ve. Moi ban ghi INVALID
        /// khac deu co toa do ("dau ho tai (x, y)"), rieng cai nay thi khong.
        ///
        /// Phep thu: dung hai duong bao chac chan cat nhau, doi thong bao phai co toa do nam
        /// trong vung giao. Chi kiem phan CHAN DOAN - ket luan hop le / khong hop le khong doi.
        /// </summary>
        private static void R26_TouchingContoursReportWhere()
        {
            // Hai hinh vuong chong len nhau mot goc.
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 100, 100), Box(60, 60, 100, 100) };
            RecognitionResult r = Recognize(c);

            // Nguoi dung yeu cau: cham / chong nhau thi GOP thanh 1 chi tiet, khong bao loi.
            Equal(1, r.Parts.Count, "hai hinh chong nhau gop thanh MOT chi tiet");
            RecognizedPart bad = r.Parts[0];
            True(bad.IsNestable, "phai ghep duoc: " + bad.NotesText);
            True(Math.Abs(bad.Outer.Area - (2 * 100 * 100 - 40 * 40)) < 1e-3,
                "duong bao ghep = hop cua hai hinh, dang co dien tich " + bad.Outer.Area);
            Equal(2, bad.GeometrySources.Count, "ca hai net deu duoc xuat");

            string note = string.Join(" | ", bad.Notes.ToArray());
            True(note.IndexOf("cham/chong", StringComparison.Ordinal) >= 0,
                "phai ghi chu la da gop, nhan duoc: " + note);
            True(note.IndexOf(" - tai (", StringComparison.Ordinal) >= 0,
                "phai noi CHO cham, nhan duoc: " + note);

            // Toa do bao ra phai nam trong vung hai hinh chong nhau (60..100 theo ca hai truc),
            // cho phep sai so dung sai noi diem.
            int at = note.IndexOf(" - tai (", StringComparison.Ordinal) + " - tai (".Length;
            string pair = note.Substring(at, note.IndexOf(')', at) - at);
            string[] xy = pair.Split(',');
            double x = double.Parse(xy[0].Trim(), CultureInfo.InvariantCulture);
            double y = double.Parse(xy[1].Trim(), CultureInfo.InvariantCulture);

            True(x >= 55 && x <= 105 && y >= 55 && y <= 105,
                "toa do phai nam o vung hai hinh chong nhau, nhan duoc (" + pair + ")");
        }

        /// <summary>
        /// Gop cham / chong nhau: lo CHAM BIEN thi bo khoi hinh ghep (coi la dac), lo KHONG dinh
        /// gi thi van la lo; chi tiet thu hai chong len thi gop vao, lo cua no nam trong vat
        /// lieu chi tiet kia thi cung bo.
        /// </summary>
        private static void R26b_TouchingMergeKeepsFreeHoles()
        {
            // (a) lo cham canh phai cua duong bao ngoai + mot lo tu do
            RecognitionResult a = Recognize(new List<CurveChain>
            {
                Box(0, 0, 200, 100),
                Box(150, 20, 50, 40),     // cham canh x = 200
                Box(20, 20, 30, 30)       // lo tu do
            });
            Equal(1, a.Parts.Count, "mot chi tiet: " + string.Join(" | ", a.Parts.ConvertAll(p => p.Status + " " + p.NotesText).ToArray()));
            True(a.Parts[0].IsNestable, "ghep duoc: " + a.Parts[0].NotesText);
            True(Math.Abs(a.Parts[0].Outer.Area - 200 * 100) < 1e-3, "duong bao van la 200x100");
            Equal(1, a.Parts[0].Holes.Count, "chi con lo tu do");
            Equal(3, a.Parts[0].GeometrySources.Count, "ca ba net deu xuat");

            // (b) chi tiet B chong mot goc len A; lo cua B nam tron trong vat lieu A -> bo
            RecognitionResult b = Recognize(new List<CurveChain>
            {
                Box(0, 0, 100, 100),
                Box(80, 80, 100, 100),
                Box(85, 85, 10, 10),      // lo cua B, nam trong A
                Box(140, 140, 20, 20)     // lo cua B, nam ngoai A -> giu
            });
            Equal(1, b.Parts.Count, "gop thanh mot: " + string.Join(" | ", b.Parts.ConvertAll(p => p.Status + " " + p.NotesText).ToArray()));
            True(b.Parts[0].IsNestable, "ghep duoc: " + b.Parts[0].NotesText);
            Equal(1, b.Parts[0].Holes.Count, "chi giu lo khong nam trong vat lieu A");
            True(Math.Abs(b.Parts[0].Outer.Area - (2 * 100 * 100 - 20 * 20)) < 1e-3, "duong bao = hop hai hinh");
        }

        /// <summary>
        /// HOI QUY (ban ve that GHOPPHOI TEST-1.dxf, chi tiet P15): hai CIRCLE ve trung khit
        /// len nhau (cung tam, cung ban kinh) lam ca chi tiet bi bao la hinh hoc loi. Thong
        /// bao cu la "cat/cham duong bao khac" khien nguoi dung di tim mot cho GIAO NHAU
        /// khong he ton tai - trong khi viec can lam chi la xoa bot mot duong trung.
        /// </summary>
        private static void R27_DuplicateContourIsMerged()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 400, 200),          // duong bao ngoai
                Box(100, 60, 80, 80),         // mot lo
                Box(100, 60, 80, 80)          // CHINH lo do, ve them mot lan nua
            };

            RecognitionResult r = Recognize(c);

            Equal(1, Valid(r).Count, "van la MOT chi tiet, khong phai hai");
            RecognizedPart p = Valid(r)[0];

            True(p.Status != PartStatus.InvalidGeometry,
                "ve trung len nhau KHONG con lam hong chi tiet: " + p.NotesText);
            Equal(1, p.Holes.Count, "hai duong trung khit chi con MOT lo");

            bool told = false;
            foreach (string w in r.GlobalWarnings)
            {
                if (w.IndexOf("TRUNG KHIT", StringComparison.Ordinal) >= 0) told = true;
            }

            True(told, "van phai BAO la da gop, khong duoc lang le: " + string.Join(" | ", r.GlobalWarnings.ToArray()));
        }

        /// <summary>
        /// Ban ve san xuat that co hang chuc net thua (duong dan, ghi chu) nam ngoai moi chi
        /// tiet - bat nguoi dung xac nhan tung cai la vo ich, nen bo tu dong.
        ///
        /// Nhung KHONG duoc bo tat: mot duong bao dinh ve kin ma bi ho mot khe nho cung roi
        /// vao day, va do lai chinh la thu nguoi dung CAN thay de sua ban ve.
        /// </summary>
        private static void R32_OpenGeometryKeptOnlyIfAlmostClosed()
        {
            // (a) hop 100x50 ho khe 2 mm -> GIU, bao loi kem toa do
            List<CurveChain> almost = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 0, 100, 50),
                Chain(false, 100, 50, 0, 50),
                Chain(false, 0, 50, 0, 2)
            };

            RecognitionResult a = Recognize(almost);
            Equal(1, a.Parts.Count, "duong bao dinh ve kin ma ho khe thi PHAI giu lai");
            True(a.Parts[0].IsNestable && a.Parts[0].NotesText.Contains("HO"), "giu lai, van ghep, va bao la duong bao ho");

            // (b) mot net thang thua -> BO, nhung co dem
            RecognitionResult b = Recognize(new List<CurveChain> { Chain(false, 0, 0, 200, 80) });
            Equal(0, b.Parts.Count, "net thua phai bi bo, khong bat nguoi dung xac nhan");

            bool counted = false;
            foreach (string w in b.GlobalWarnings)
            {
                if (w.IndexOf("nam ngoai moi chi tiet", StringComparison.Ordinal) >= 0) counted = true;
            }

            True(counted, "bo thi phai DEM va bao, khong duoc lang le");

            // (c) chu L thua (hai dau xa nhau) -> BO
            RecognitionResult d = Recognize(new List<CurveChain>
            {
                Chain(false, 0, 0, 200, 0),
                Chain(false, 200, 0, 200, 150)
            });

            Equal(0, d.Parts.Count, "hai dau xa nhau thi khong phai chi tiet");
        }

        /// <summary>
        /// Chu tren layer khac nam TRONG chi tiet: phai duoc gan de XUAT ra ban ve, nhung
        /// khong duoc dem vao MarkingSources (con so do duoc bao cho nguoi dung la "co N
        /// duong ho ben trong", ma chu khac thi khong phai duong ho).
        ///
        /// Va quan trong nhat: KHONG duoc dua vao hinh ghep - chu khac la hinh khac len be
        /// mat, khong phai duong cat.
        /// </summary>
        private static void R28_EngravingInsideAttaches()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 400, 200) });
            RecognizedPart part = r.Parts[0];

            int outerPointsBefore = part.Outer.Points.Count;
            int holesBefore = part.Holes.Count;

            True(PartRecognizer.AttachEngraving(r.Parts, 901, new Pt(200, 100)), "phai gan duoc");

            True(part.EngravingSources.Contains(901), "phai vao EngravingSources");
            True(part.GeometrySources.Contains(901), "phai vao GeometrySources de duoc xuat");
            True(!part.MarkingSources.Contains(901), "KHONG duoc dem vao MarkingSources");

            Equal(outerPointsBefore, part.Outer.Points.Count, "duong bao ngoai khong doi");
            Equal(holesBefore, part.Holes.Count, "so lo khong doi - chu khac khong phai lo");
        }

        /// <summary>Chu khac khong nam trong chi tiet nao thi khong gan - de nguoi goi bao ra.</summary>
        private static void R29_EngravingOutsideNotAttached()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 400, 200) });

            True(!PartRecognizer.AttachEngraving(r.Parts, 902, new Pt(900, 900)), "ngoai het -> khong gan");
            True(!PartRecognizer.AttachEngraving(r.Parts, 903, new Pt(-5, 100)), "ngay ben canh cung khong gan");
            Equal(0, r.Parts[0].EngravingSources.Count, "khong co chu khac nao duoc gan");
        }

        /// <summary>
        /// Chi tiet nho nam trong LO cua chi tiet lon (truong hop da duoc ho tro): chu khac
        /// dat trong chi tiet nho phai thuoc ve chi tiet NHO, khong phai chi tiet bao ngoai.
        /// </summary>
        private static void R30_EngravingInnermostWins()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 400, 400),        // chi tiet lon
                Box(50, 50, 300, 300),      // lo cua no
                Box(120, 120, 160, 160)     // chi tiet nho nam trong lo
            };

            RecognitionResult r = Recognize(c);
            Equal(2, Valid(r).Count, "hai chi tiet");

            True(PartRecognizer.AttachEngraving(r.Parts, 904, new Pt(200, 200)), "gan duoc");

            RecognizedPart small = null, big = null;
            foreach (RecognizedPart p in Valid(r))
            {
                if (p.Outer.Area < 200.0 * 200.0) small = p;
                else big = p;
            }

            True(small != null && big != null, "tim duoc ca hai chi tiet");
            True(small.EngravingSources.Contains(904), "chu khac thuoc chi tiet NHO");
            True(!big.EngravingSources.Contains(904), "chi tiet lon KHONG duoc nhan");
        }

        /// <summary>Nhieu chu khac tren nhieu chi tiet: moi chu ve dung chi tiet cua no.</summary>
        private static void R31_EngravingManyPartsSeparate()
        {
            RecognitionResult r = Recognize(new List<CurveChain>
            {
                Box(0, 0, 200, 100),
                Box(400, 0, 200, 100)
            });

            RecognizedPart a = Valid(r).Find(p => p.MinX < 1);
            RecognizedPart b = Valid(r).Find(p => p.MinX > 1);

            True(PartRecognizer.AttachEngraving(r.Parts, 910, new Pt(50, 50)), "A1");
            True(PartRecognizer.AttachEngraving(r.Parts, 911, new Pt(150, 50)), "A2");
            True(PartRecognizer.AttachEngraving(r.Parts, 912, new Pt(500, 50)), "B1");

            Equal(2, a.EngravingSources.Count, "chi tiet A co 2 chu khac");
            Equal(1, b.EngravingSources.Count, "chi tiet B co 1 chu khac");
            True(a.EngravingSources.Contains(910) && a.EngravingSources.Contains(911), "dung 2 chu cua A");
            True(b.EngravingSources.Contains(912), "dung chu cua B");

            // Gan lai cung mot nguon khong duoc nhan doi.
            PartRecognizer.AttachEngraving(r.Parts, 910, new Pt(50, 50));
            Equal(2, a.EngravingSources.Count, "gan lai khong duoc nhan doi");
        }

        /// <summary>
        /// Chu nam TRONG duong bao la CHU CAT tren chi tiet (ma chi tiet) - may se cat no that,
        /// nen no phai di theo chi tiet ra ban ve moi va xoay / lat cung chi tiet.
        ///
        /// Nhung chu doc ra duoc SL / vat lieu thi la THONG TIN, khong phai thu de cat - chi doc
        /// roi thoi. Phan biet bang chinh noi dung, khong bat nguoi dung them layer.
        /// </summary>
        private static void R33_InsideCutTextTravelsWithPart()
        {
            // A: ma chi tiet nam trong phoi   B: SL nam trong phoi
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 400, 200), Box(600, 0, 400, 200) };
            RecognitionResult r = Recognize(c,
                Text("P-L-347-566-1", 200, 100),
                Text("SL: 5", 800, 100));

            RecognizedPart a = Valid(r).Find(p => p.MinX < 1);
            RecognizedPart b = Valid(r).Find(p => p.MinX > 1);
            True(a != null && b != null, "hai chi tiet");

            // A: ma chi tiet -> chu CAT, phai di theo
            Equal(1, a.EngravingSources.Count, "ma chi tiet trong phoi phai di theo phoi");
            True(a.GeometrySources.Contains(a.EngravingSources[0]),
                "phai nam trong GeometrySources thi moi duoc xuat ra ban ve");
            True(a.Name.IndexOf("P-L-347-566-1", StringComparison.Ordinal) >= 0,
                "va van dung lam ten cho de nhan mat, nhan duoc: " + a.Name);

            // B: SL -> thong tin, chi doc, KHONG cat
            Equal(5, b.Quantity, "SL van phai doc duoc");
            Equal(0, b.EngravingSources.Count, "chu THONG TIN khong duoc mang di cat");

            // Chu nam NGOAI moi phoi thi cung khong duoc mang di cat.
            RecognitionResult outside = Recognize(
                new List<CurveChain> { Box(0, 0, 400, 200) },
                Text("GHI CHU CHUNG", 900, 900));
            Equal(0, Valid(outside)[0].EngravingSources.Count, "chu ngoai phoi khong di theo phoi nao");
        }

        private static CurveChain Box(double x, double y, double w, double h)
        {
            return Chain(true, x, y, x + w, y, x + w, y + h, x, y + h);
        }

        private static TextItem Text(string s, double x, double y)
        {
            return new TextItem(_src++, s, new Pt(x, y));
        }

        private static RecognitionResult Recognize(List<CurveChain> chains, params TextItem[] texts)
        {
            return new PartRecognizer(new RecognitionSettings()).Recognize(chains, texts);
        }

        private static List<RecognizedPart> Valid(RecognitionResult r)
        {
            return r.Parts.FindAll(p => p.Outer != null);
        }

        private static void R01_QuantityFormats()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            foreach (string s in new[] { "SL: 12", "SL:12", "SL 12", "sl: 12", "SL=12", "  SL :  12 " })
            {
                List<MetadataFact> f = p.Parse(s);
                True(f.Count == 1 && f[0].Kind == MetadataKind.Quantity && f[0].QuantityValue == 12, "parse '" + s + "'");
            }

            List<MetadataFact> both = p.Parse("SL: 4\n1.2MM");
            Equal(2, both.Count, "SL and material in one MTEXT");
        }

        private static void R02_MaterialFormats()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            foreach (string s in new[] { "1.2MM", "1.2 MM", "1,2MM", "1,2 MM", "1.2mm" })
            {
                List<MetadataFact> f = p.Parse(s);
                True(f.Count == 1 && f[0].Kind == MetadataKind.Material && f[0].Value == "1.2MM", "parse '" + s + "'");
            }

            // Co ghi loai: do day van doc dung, CONG them loai vat lieu (xem R47).
            List<MetadataFact> inox = p.Parse("INOX 1.2MM");
            True(inox.Exists(x => x.Kind == MetadataKind.Material && x.Value == "1.2MM"), "parse 'INOX 1.2MM' - do day");
            True(inox.Exists(x => x.Kind == MetadataKind.MaterialType && x.Value == "INOX"), "parse 'INOX 1.2MM' - loai");

            Equal("1.5MM", p.Parse("1.50 mm")[0].Value, "normalised");
            Equal("2MM", p.Parse("2MM")[0].Value, "integer thickness");
        }

        private static void R03_NotTooPermissive()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            Equal(0, p.Parse("150MM").Count, "150 mm is a dimension, not a thickness");
            Equal(0, p.Parse("ASL 3").Count, "SL must not be part of another word");
            Equal(0, p.Parse("SL: 0").Count, "quantity 0 rejected");
            Equal(0, p.Parse("SL: 1.5").Count, "decimal quantity rejected");
            Equal(0, p.Parse("MAT BICH").Count, "plain text");
            Equal(0, p.Parse("12.5.3MM").Count, "garbage number");
        }

        private static void R04_LinesFormOnePart()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 50, 0, 50),          // drawn in reverse direction
                Chain(false, 100, 0, 100, 50),
                Chain(false, 0, 50, 0.01, 0.01)        // 0.014 mm gap -> joined (tol 0.05)
            };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "one record");
            RecognizedPart part = r.Parts[0];
            True(part.IsNestable, "valid part: " + part.NotesText);
            Equal(4, part.GeometrySources.Count, "all 4 lines belong to the part");
            Close(5000, part.Outer.Area, 2, "area");
        }

        private static void R05_HoleAndPartInHole()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 300, 300),
                Box(50, 50, 200, 200),     // hole
                Box(100, 100, 50, 50)      // separate part inside the hole
            };
            List<RecognizedPart> parts = Valid(Recognize(c));
            Equal(2, parts.Count, "frame + inner part");
            RecognizedPart frame = parts.Find(p => p.Outer.Area > 80000);
            Equal(1, frame.Holes.Count, "frame has one hole");
            RecognizedPart inner = parts.Find(p => p.Outer.Area < 5000);
            Equal(0, inner.Holes.Count, "inner part has no hole");
        }

        private static void R06_OpenContourInvalid()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 0, 100, 50),
                Chain(false, 100, 50, 0, 50),
                Chain(false, 0, 50, 0, 2)              // 2 mm gap
            };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "one record");
            RecognizedPart p = r.Parts[0];

            // KHONG bo: duong bao ho van la MOT phoi, tu noi khe ho de ghep.
            True(p.IsNestable && p.Include, "duong bao ho van duoc ghep");
            Equal(PartStatus.Warning, p.Status, "chi la canh bao");
            True(p.NotesText.Contains("HO"), "van noi ro la duong bao ho: " + p.NotesText);
            True(p.NotesText.Contains("DA TU SUA"), "noi ro da tu sua");
            Close(100.0 * 50.0, p.Outer.Area, 1.0, "noi khe ho -> dung hinh chu nhat 100 x 50");
            AssertCoversChains(p, c);
            AssertNestsValid(r);
        }

        /// <summary>Hinh dung de ghep phai CHUA moi diem cua moi net that (an toan khe cat).</summary>
        private static void AssertCoversChains(RecognizedPart p, IEnumerable<CurveChain> chains)
        {
            List<IntPoint> ring = new List<IntPoint>();
            foreach (Pt q in p.Outer.Points) ring.Add(IntPoint.FromMm(q.X, q.Y));
            foreach (CurveChain ch in chains)
            {
                foreach (Pt q in ch.Points)
                {
                    True(GeometryMath.PointInRing(IntPoint.FromMm(q.X, q.Y), ring) >= 0, "net that nam trong hinh ghep: " + q);
                }
            }
        }

        /// <summary>Ban ghi da sua phai di qua ghep that + validator.</summary>
        private static void AssertNestsValid(RecognitionResult r)
        {
            foreach (RecognizedPart p in r.Parts) p.Confirmed = true;
            NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("K", 2000, 1000), Settings = new NestingSettings() };
            req.Groups.AddRange(PartRecognizer.ToPartGroups(r.Parts, 0.05));
            NestingResult res = new SimpleNestingEngine().Nest(req, System.Threading.CancellationToken.None, null);
            True(res.Validation.IsValid, "validator");
            Equal(0, res.Statistics.UnplacedQuantity, "xep het");
        }

        private static void R07_BendLineInsideIsMarking()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 200, 100),
                Chain(false, 60, 0, 60, 100),          // bend line touching the outline
                Chain(false, 140, 0, 140, 100)
            };
            List<RecognizedPart> parts = Valid(Recognize(c));
            Equal(1, parts.Count, "one part");
            True(parts[0].IsNestable, "still nestable");
            Equal(2, parts[0].MarkingSources.Count, "2 markings kept for output");
        }

        private static void R08_SelfIntersectionInvalid()
        {
            List<CurveChain> c = new List<CurveChain> { Chain(true, 0, 0, 100, 100, 100, 0, 0, 100) };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "mot phoi");
            True(r.Parts[0].IsNestable && r.Parts[0].Include, "tu cat van duoc ghep");
            Equal(PartStatus.Warning, r.Parts[0].Status, "chi la canh bao");
            True(r.Parts[0].NotesText.Contains("DA TU SUA"), "noi ro da tu sua: " + r.Parts[0].NotesText);
            AssertCoversChains(r.Parts[0], c);
            AssertNestsValid(r);
        }

        private static void R09_TextInside()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(300, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 50, 50), Text("1.5MM", 60, 30), Text("SL: 4", 400, 50));
            List<RecognizedPart> parts = Valid(r);
            RecognizedPart a = parts.Find(p => p.MinX < 1), b = parts.Find(p => p.MinX > 1);
            Equal(12, a.Quantity, "A qty");
            Equal("1.5MM", a.Material, "A material");
            Equal(4, b.Quantity, "B qty");
            Equal("1.2MM", b.Material, "B default material");
            Equal(PartStatus.Ok, a.Status, "A ok");
        }

        private static void R10_TextOutsideNearest()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(600, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 100, -20), Text("1.2MM", 100, -40), Text("SL: 4", 700, 130));
            List<RecognizedPart> parts = Valid(r);
            RecognizedPart a = parts.Find(p => p.MinX < 1), b = parts.Find(p => p.MinX > 1);
            Equal(12, a.Quantity, "A takes the text below it");
            Equal(4, b.Quantity, "B takes the text above it");
            True(a.QuantityFromText && a.MaterialFromText, "A from text");
        }

        private static void R11_TextAmbiguous()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(260, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 230, 50));   // 30 mm from both
            foreach (RecognizedPart p in Valid(r))
            {
                Equal(PartStatus.Ambiguous, p.Status, "both flagged");
                Equal(1, p.Quantity, "value not silently assigned");
            }
        }

        private static void R12_Defaults()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) });
            RecognizedPart p = Valid(r)[0];
            Equal(1, p.Quantity, "default qty");
            Equal("1.2MM", p.Material, "default material");
            Equal(PartStatus.Warning, p.Status, "defaults are visible as warning");
            True(p.NotesText.Contains("mac dinh"), "note explains default");
        }

        private static RecognizedPart At(RecognitionResult r, double minX)
        {
            RecognizedPart p = Valid(r).Find(x => Math.Abs(x.Outer.MinX - minX) < 1e-6);
            if (p == null) throw new NestingAssertException("khong tim thay chi tiet tai x = " + minX);
            return p;
        }

        private static void R34_QuantityVariants()
        {
            string[] texts = { "SL: 0010", "SL: 2 cai", "SL: 3 (bo)", "SL: 99999" };
            int[] want = { 10, 2, 3, 99999 };
            for (int i = 0; i < texts.Length; i++)
            {
                RecognizedPart p = Valid(Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text(texts[i], 50, 50), Text("1.2MM", 50, 20)))[0];
                Equal(want[i], p.Quantity, "'" + texts[i] + "'");
                Equal(PartStatus.Ok, p.Status, "'" + texts[i] + "' ro rang -> OK");
            }
        }

        private static void R35_DuplicateMetadataNotAmbiguous()
        {
            RecognizedPart p = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) },
                Text("SL: 2", 20, 80), Text("SL: 2", 20, 20), Text("1.2MM", 150, 80), Text("1,2 mm", 150, 20)))[0];
            Equal(2, p.Quantity, "SL lap lai giong nhau");
            Equal("1.2MM", p.Material, "1.2MM == 1,2 mm");
            Equal(PartStatus.Ok, p.Status, "trung GIONG nhau khong phai mo ho");
        }

        private static void R36_ConflictingMaterials()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 2", 20, 50), Text("1.2MM", 100, 70), Text("1.5MM", 100, 30));
            RecognizedPart p = Valid(r)[0];
            Equal(PartStatus.Ambiguous, p.Status, "hai vat lieu khac nhau");

            bool threw = false;
            try
            {
                PartRecognizer.ToPartGroups(r.Parts, 0.05);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            True(threw, "chua xac nhan -> khong duoc dua vao ghep");
            p.Confirmed = true;
            Equal(1, PartRecognizer.ToPartGroups(r.Parts, 0.05).Count, "xac nhan roi thi di tiep");
        }

        private static void R37_TwoQuantitiesOneText()
        {
            RecognizedPart p = Valid(Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 2 SL: 3", 50, 50)))[0];
            Equal(PartStatus.Ambiguous, p.Status, "hai SL trong mot chu");
        }

        /// <summary>
        /// Nguong mo ho = max(0.5 mm, 1% khoang cach). Hai chi tiet cach nhau 60 mm, text o giua:
        /// chenh 0.4 mm -> mo ho (ca hai bi bao, khong gan); chenh 0.6 mm -> chi tiet gan hon.
        /// </summary>
        private static void R38_AmbiguityBoundary()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(260, 0, 200, 100) };
            RecognitionResult near = Recognize(c, Text("SL: 7", 229.8, 50));       // 29.8 / 30.2
            Equal(PartStatus.Ambiguous, At(near, 0).Status, "chenh 0.4 mm: A mo ho");
            Equal(PartStatus.Ambiguous, At(near, 260).Status, "chenh 0.4 mm: B mo ho");
            Equal(1, At(near, 0).Quantity, "mo ho: khong gan im lang");

            RecognitionResult clear = Recognize(c, Text("SL: 7", 229.7, 50));      // 29.7 / 30.3
            Equal(7, At(clear, 0).Quantity, "chenh 0.6 mm: A gan hon nhan SL");
            Equal(1, At(clear, 260).Quantity, "B khong nhan");
            True(At(clear, 0).Status != PartStatus.Ambiguous, "khong mo ho");
        }

        private static void R39_NeighbourQuantityFlagged()
        {
            // A co SL 3 ben trong; SL 7 nam ngoai, gan vien A hon vien B -> A nhan HAI SL khac nhau -> mo ho.
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(300, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 3", 100, 50), Text("SL: 7", 220, 50));
            Equal(PartStatus.Ambiguous, At(r, 0).Status, "A: hai SL khac nhau -> phai bao");
            True(At(r, 300).Status != PartStatus.Ok, "B: khong co SL -> it nhat WARNING, khong im lang");
        }

        // ==================================================================================
        // METADATA A-D: SL sai, dau phay, kich thuoc / so am, chu trong lo
        // ==================================================================================

        /// <summary>
        /// Mot dong ky vong: qty = SL hop le (0 = khong co), mat = vat lieu hop le (null = khong
        /// co), issue = chuoi loai van de mong doi ("Q" SL sai, "M" vat lieu am, "D" kich thuoc,
        /// ghep lai theo thu tu; "" = khong co van de).
        /// </summary>
        private static void ExpectReading(MetadataParser p, string text, int qty, string mat, string issues)
        {
            MetadataReading r = p.Classify(text);
            MetadataFact q = r.Facts.Find(f => f.Kind == MetadataKind.Quantity);
            MetadataFact m = r.Facts.Find(f => f.Kind == MetadataKind.Material);
            string got = string.Empty;
            foreach (MetadataIssue i in r.Issues)
            {
                got += i.Kind == MetadataIssueKind.InvalidQuantity ? "Q" : i.Kind == MetadataIssueKind.InvalidMaterial ? "M" : "D";
            }

            string where = "'" + text + "'";
            Equal(qty, q == null ? 0 : q.QuantityValue, where + " SL");
            Equal(mat, m == null ? null : m.Value, where + " vat lieu");
            Equal(issues, got, where + " van de");
            Equal(qty == 0 ? 0 : 1, r.Facts.FindAll(f => f.Kind == MetadataKind.Quantity).Count, where + " so SL");
        }

        /// <summary>A + B: SL hop le / SL co dang nhung sai / khong phai SL; dau phay sau SL.</summary>
        private static void R40_QuantityClassification()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());

            // SL co dang ro rang nhung gia tri SAI -> van de Q, khong co SL.
            foreach (string bad in new[] { "SL: 0", "SL: -1", "SL: abc", "SL: 1.5", "SL: 99999999999", "SL:", "SL: ???", "SL: 1,5", "SL -1", "SL=+3", "SL: 100001", "SL: 00000" })
            {
                ExpectReading(p, bad, 0, null, "Q");
            }

            // Hop le (ke ca so 0 o dau, chu theo sau, chu thuong, khoang trang Unicode / tab).
            ExpectReading(p, "SL: 10", 10, null, "");
            ExpectReading(p, "SL: 0010", 10, null, "");
            ExpectReading(p, "SL: 2 cai", 2, null, "");
            ExpectReading(p, "sl: 2", 2, null, "");
            ExpectReading(p, "SL : 2", 2, null, "");
            ExpectReading(p, "SL : 2", 2, null, "");
            ExpectReading(p, "SL:\t7", 7, null, "");
            ExpectReading(p, "SL 5", 5, null, "");
            ExpectReading(p, "SL=12", 12, null, "");
            ExpectReading(p, "SL: 100000", 100000, null, "");
            ExpectReading(p, "SL: 5MM", 5, null, "");

            // Khong phai SL: tu khac, SL dinh chu, "SL" dung mot minh / theo sau la chu.
            foreach (string plain in new[] { "SLOT", "SLIDE", "SLEEVE", "SLOT 5", "ASL 3", "SL ABC", "SL", "SLS: 2" })
            {
                ExpectReading(p, plain, 0, null, "");
            }

            // B: dau phay / khoang trang sau SL.
            ExpectReading(p, "SL: 2, 1.2MM", 2, "1.2MM", "");
            ExpectReading(p, "SL: 10,1.5MM", 10, "1.5MM", "");
            ExpectReading(p, "SL: 0010, 1.2MM", 10, "1.2MM", "");
            ExpectReading(p, "SL: 2 , 1.2MM", 2, "1.2MM", "");
            ExpectReading(p, "SL: 2,", 2, null, "");
            ExpectReading(p, "SL: 2, abc", 2, null, "");
            ExpectReading(p, "SL: 2.5, 1.2MM", 0, "1.2MM", "Q");
            ExpectReading(p, "SL: 0, 1.2MM", 0, "1.2MM", "Q");
            ExpectReading(p, "SL: -2, 1.2MM", 0, "1.2MM", "Q");

            // Hop dong cu cua Parse(): chi gia tri hop le.
            Equal(0, p.Parse("SL: abc").Count, "Parse khong tra ve SL sai");
            Equal(2, p.Parse("SL: 2, 1.2MM").Count, "Parse doc ca SL lan vat lieu");
        }

        /// <summary>C: vat lieu hop le / kich thuoc / so am / khong phai thong tin.</summary>
        private static void R41_MaterialVersusDimension()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());

            ExpectReading(p, "1.2MM", 0, "1.2MM", "");
            ExpectReading(p, "1.5MM", 0, "1.5MM", "");
            ExpectReading(p, "2MM", 0, "2MM", "");
            ExpectReading(p, "2.0MM", 0, "2MM", "");
            ExpectReading(p, "1,2 mm", 0, "1.2MM", "");
            ExpectReading(p, "1.2 MM", 0, "1.2MM", "");
            ExpectReading(p, "1.2mm", 0, "1.2MM", "");
            ExpectReading(p, "T1.2MM", 0, "1.2MM", "");
            ExpectReading(p, "0.3MM", 0, "0.3MM", "");
            ExpectReading(p, "25MM", 0, "25MM", "");
            // Quy uoc CO SAN (R23, MaxThicknessMm = 25): so tron 0.3..25 mm la do day - tam 20 mm la
            // vat lieu that. Chi co tien to R / D / O, hoac ngoai khoang, moi la kich thuoc.
            ExpectReading(p, "20MM", 0, "20MM", "");

            foreach (string dim in new[] { "R12.5MM", "R20MM", "D10MM", "D100MM", "100MM", "150MM", "Ø8MM", "r5mm", "0.1MM", "30MM", "1200 MM", "1.2345MM" })
            {
                ExpectReading(p, dim, 0, null, "D");
            }

            foreach (string neg in new[] { "-1.2MM", "-20MM", "-150MM", "-0.5MM" })
            {
                ExpectReading(p, neg, 0, null, "M");
            }

            // Khong co so -> khong phai thong tin (xu ly nhu chu thuong).
            foreach (string plain in new[] { "abcMM", ".MM", "MM", "COMM", "12.5.3MM" })
            {
                ExpectReading(p, plain, 0, null, "");
            }

            ExpectReading(p, "SL: 2, R12.5MM", 2, null, "D");
            ExpectReading(p, "SL: 2, D10MM", 2, null, "D");
            ExpectReading(p, "SL: 2, -1.2MM", 2, null, "M");
            ExpectReading(p, "1.2MM 1.5MM", 0, "1.2MM", "");
            Equal(2, p.Classify("1.2MM 1.5MM").Facts.Count, "hai vat lieu trong mot chu -> hai gia tri (associator bao mo ho)");
        }

        /// <summary>A qua duong nhan dang: chu SL sai -> MO HO co ghi chu, KHONG thanh chu cat, khong gan im lang.</summary>
        private static void R42_InvalidMetadataRecognition()
        {
            foreach (string bad in new[] { "SL: abc", "SL: 0", "SL: 1.5", "SL: 99999999999", "SL:", "SL: -1" })
            {
                RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text(bad, 100, 50), Text("1.2MM", 100, 20));
                RecognizedPart p = Valid(r)[0];
                Equal(PartStatus.Ambiguous, p.Status, "'" + bad + "' trong chi tiet -> phai xac nhan");
                Equal(0, p.EngravingSources.Count, "'" + bad + "' KHONG duoc thanh chu cat");
                Equal(1, p.Quantity, "'" + bad + "' tam 1");
                True(p.NotesText.Contains(bad.Trim()), "ghi chu phai chi ra chu '" + bad + "': " + p.NotesText);
                True(p.Name.IndexOf(bad, StringComparison.Ordinal) < 0, "'" + bad + "' khong duoc thanh ten chi tiet");

                bool threw = false;
                try
                {
                    PartRecognizer.ToPartGroups(r.Parts, 0.05);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                True(threw, "'" + bad + "': chua xac nhan thi KHONG duoc ghep");
            }

            // Ngoai chi tiet: truoc day bi bo IM LANG.
            RecognitionResult outside = Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: abc", 100, -30));
            Equal(PartStatus.Ambiguous, Valid(outside)[0].Status, "SL sai ngoai chi tiet (gan) -> van phai bao");

            RecognitionResult far = Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: abc", 5000, 5000));
            Equal(1, far.GlobalWarnings.Count, "SL sai qua xa -> canh bao chung");

            // Mot SL dung + mot SL sai: van lay SL dung nhung PHAI xac nhan.
            RecognizedPart mixed = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 2", 50, 50), Text("SL: abc", 150, 50)))[0];
            Equal(2, mixed.Quantity, "SL dung van duoc doc");
            Equal(PartStatus.Ambiguous, mixed.Status, "co chu SL sai -> phai xac nhan");

            // Vat lieu am.
            RecognizedPart neg = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 2", 50, 50), Text("-1.2MM", 150, 50)))[0];
            Equal(PartStatus.Ambiguous, neg.Status, "vat lieu am -> phai xac nhan");
            Equal("1.2MM", neg.Material, "tam mac dinh (khong phai doc tu chu am)");
            True(!neg.MaterialFromText, "vat lieu KHONG duoc coi la doc tu chu");
            True(neg.NotesText.Contains("-1.2MM"), "ghi chu chi ra chu am");
            Equal(0, neg.EngravingSources.Count, "chu vat lieu am khong thanh chu cat");

            // Kich thuoc trong chi tiet: khong phai thong tin, KHONG thanh chu cat.
            foreach (string dim in new[] { "R12.5MM", "D10MM", "150MM" })
            {
                RecognizedPart d = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 3", 50, 50), Text(dim, 150, 50)))[0];
                Equal(0, d.EngravingSources.Count, "'" + dim + "' khong duoc mang di cat");
                Equal(3, d.Quantity, "SL van dung");
                Equal("1.2MM", d.Material, "'" + dim + "' khong phai vat lieu");
                Equal(PartStatus.Warning, d.Status, "thieu vat lieu -> WARNING nhu truoc, khong mo ho gia");
            }

            // B qua duong nhan dang.
            RecognizedPart comma = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 2, 1.2MM", 100, 50)))[0];
            Equal(2, comma.Quantity, "SL: 2, 1.2MM -> SL 2");
            Equal("1.2MM", comma.Material, "... va 1.2MM");
            Equal(PartStatus.Ok, comma.Status, "ro rang -> OK");
        }

        /// <summary>
        /// Khung 400x400 co lo 300x300 (50..350), chi tiet nho trong lo. Cac chi tiet va chu dung
        /// cho D (xem R43).
        /// </summary>
        private static List<CurveChain> FrameWithHole(double ox, double oy, params double[][] inner)
        {
            List<CurveChain> c = new List<CurveChain> { Box(ox, oy, 400, 400), Box(ox + 50, oy + 50, 300, 300) };
            foreach (double[] b in inner) c.Add(Box(ox + b[0], oy + b[1], b[2], b[3]));
            return c;
        }

        /// <summary>
        /// D: chu trong LO cua khung. Uu tien: trong VAT LIEU cua chi tiet nho nhat -> chi tiet
        /// do; khong thi chi tiet co HINH (vong ngoai hoac mep lo) gan nhat; hai ben ngang nhau -> mo
        /// ho. Diem nam trong lo KHONG thuoc khung.
        /// </summary>
        private static void R43_TextInsideHoleTopology()
        {
            double[] small = { 120, 120, 100, 100 };

            // 1. Chu cua khung, tren vat lieu khung.
            RecognitionResult r1 = Recognize(FrameWithHole(0, 0, small), Text("SL: 3", 20, 200));
            Equal(3, At(r1, 0).Quantity, "1: khung nhan chu tren vat lieu khung");
            Equal(1, At(r1, 120).Quantity, "1: chi tiet nho khong nhan");

            // 2. Chu cua chi tiet nho, trong chi tiet nho.
            RecognitionResult r2 = Recognize(FrameWithHole(0, 0, small), Text("SL: 4", 170, 170));
            Equal(4, At(r2, 120).Quantity, "2: chi tiet nho nhan chu cua no");
            Equal(1, At(r2, 0).Quantity, "2: khung KHONG nhan");

            // 3. Ca hai co chu rieng.
            RecognitionResult r3 = Recognize(FrameWithHole(0, 0, small), Text("SL: 5", 20, 20), Text("SL: 6", 170, 170));
            Equal(5, At(r3, 0).Quantity, "3: khung");
            Equal(6, At(r3, 120).Quantity, "3: chi tiet nho");
            Equal(PartStatus.Warning, At(r3, 0).Status, "3: khung khong mo ho (chi thieu vat lieu)");

            // 4. Nhieu chi tiet nho trong cung lo.
            RecognitionResult r4 = Recognize(FrameWithHole(0, 0, new double[] { 70, 70, 60, 60 }, new double[] { 250, 250, 60, 60 }),
                Text("SL: 7", 100, 100), Text("SL: 8", 280, 280));
            Equal(7, At(r4, 70).Quantity, "4: chi tiet nho thu nhat");
            Equal(8, At(r4, 250).Quantity, "4: chi tiet nho thu hai");
            Equal(1, At(r4, 0).Quantity, "4: khung khong nhan");

            // 5. Lo long lo: khung > lo > S (co lo rieng) > T trong lo cua S.
            List<CurveChain> nested = FrameWithHole(0, 0, new double[] { 100, 100, 200, 200 }, new double[] { 150, 150, 100, 100 }, new double[] { 180, 180, 40, 40 });
            RecognitionResult r5 = Recognize(nested, Text("SL: 9", 200, 200), Text("SL: 2", 120, 120));
            Equal(9, At(r5, 180).Quantity, "5: T (sau nhat) nhan chu cua no");
            Equal(2, At(r5, 100).Quantity, "5: S nhan chu tren vat lieu S");
            Equal(1, At(r5, 0).Quantity, "5: khung khong nhan");

            // 6. Chu o khoang trong cua lo, xa chi tiet nho: gan MEP LO cua khung nhat -> khung.
            RecognitionResult r6 = Recognize(FrameWithHole(0, 0, small), Text("SL: 2", 330, 330));
            Equal(2, At(r6, 0).Quantity, "6: gan mep lo khung (20 mm) hon chi tiet nho (~156 mm) -> khung");
            Equal(1, At(r6, 120).Quantity, "6: chi tiet nho khong nhan");

            // 7. Gan ca khung lan chi tiet nho, ro rang gan chi tiet nho hon.
            RecognitionResult r7 = Recognize(FrameWithHole(0, 0, small), Text("SL: 4", 235, 170));
            Equal(4, At(r7, 120).Quantity, "7: cach chi tiet nho 15 mm, mep lo 115 mm -> chi tiet nho");
            Equal(1, At(r7, 0).Quantity, "7: khung khong nhan");

            // 8. Dung tren bien: bien chi tiet nho -> chi tiet nho; bien lo -> thuoc vat lieu khung.
            RecognitionResult r8a = Recognize(FrameWithHole(0, 0, small), Text("SL: 4", 220, 170));
            Equal(4, At(r8a, 120).Quantity, "8: tren bien chi tiet nho");
            RecognitionResult r8b = Recognize(FrameWithHole(0, 0, small), Text("SL: 5", 50, 200));
            Equal(5, At(r8b, 0).Quantity, "8: tren mep lo -> vat lieu khung");

            // 9. Cach deu hai chi tiet nho -> MO HO, khong doan.
            RecognitionResult r9 = Recognize(FrameWithHole(0, 0, new double[] { 100, 150, 50, 50 }, new double[] { 170, 150, 50, 50 }), Text("SL: 6", 160, 175));
            Equal(PartStatus.Ambiguous, At(r9, 100).Status, "9: ben trai mo ho");
            Equal(PartStatus.Ambiguous, At(r9, 170).Status, "9: ben phai mo ho");
            Equal(1, At(r9, 100).Quantity, "9: khong gan");
            Equal(1, At(r9, 170).Quantity, "9: khong gan");

            // 10. SL + vat lieu cua chi tiet nho trong lo.
            RecognitionResult r10 = Recognize(FrameWithHole(0, 0, small), Text("SL: 3", 170, 190), Text("1.5MM", 170, 140));
            Equal(3, At(r10, 120).Quantity, "10: SL chi tiet nho");
            Equal("1.5MM", At(r10, 120).Material, "10: vat lieu chi tiet nho");
            Equal("1.2MM", At(r10, 0).Material, "10: khung giu mac dinh");

            // Chu CAT (khong phai thong tin) nam trong khoang trong cua lo: khong phai chu cat cua khung.
            RecognitionResult cut = Recognize(FrameWithHole(0, 0, small), Text("KH-01", 330, 330));
            Equal(0, At(cut, 0).EngravingSources.Count, "chu trong lo khong duoc cat vao khung (khoang trong)");
            RecognitionResult cutSmall = Recognize(FrameWithHole(0, 0, small), Text("KH-02", 170, 170));
            Equal(1, At(cutSmall, 120).EngravingSources.Count, "chu cat trong chi tiet nho van di theo chi tiet nho");

            // Chu tren layer khac: cung quy tac vat lieu.
            RecognitionResult eng = Recognize(FrameWithHole(0, 0, small));
            True(!PartRecognizer.AttachEngraving(eng.Parts, 901, new Pt(330, 330)), "chu khac trong khoang trong cua lo -> khong gan (se bao)");
            True(PartRecognizer.AttachEngraving(eng.Parts, 902, new Pt(20, 20)), "tren vat lieu khung -> gan");
            Equal(1, At(eng, 0).EngravingSources.Count, "khung chi nhan chu tren vat lieu cua no");
        }

        /// <summary>
        /// R1: cung ban ve dat o goc toa do rat xa (+-1e6, -3e6, 1e7 mm) phai cho DUNG cung ket
        /// qua: nhan dang lo / chi tiet trong lo, gan chu, mo ho. Truoc day PointInRing chay tren
        /// toa do the gioi va co the tran long khi diem xa vong ~3e6 mm.
        /// </summary>
        private static void R44_FarCoordinates()
        {
            Func<double, double, string> run = (ox, oy) =>
            {
                List<CurveChain> c = FrameWithHole(ox, oy, new double[] { 120, 120, 100, 100 });
                c.Add(Box(ox + 5000, oy, 200, 100));                                          // chi tiet thu ba o xa
                RecognitionResult r = Recognize(c,
                    Text("SL: 3", ox + 20, oy + 200), Text("SL: 4", ox + 170, oy + 170),
                    Text("SL: 2", ox + 330, oy + 330), Text("SL: abc", ox + 5100, oy + 50),
                    Text("SL: 9", ox + 3000000, oy + 3000000));                               // rat xa moi thu
                StringBuilder sb = new StringBuilder();
                List<RecognizedPart> parts = Valid(r);
                parts.Sort((a, b) => a.Outer.MinX.CompareTo(b.Outer.MinX) != 0 ? a.Outer.MinX.CompareTo(b.Outer.MinX) : a.Outer.MinY.CompareTo(b.Outer.MinY));
                foreach (RecognizedPart p in parts)
                {
                    sb.Append(p.Outer.MinX - ox).Append(',').Append(p.Holes.Count).Append(':').Append(p.Quantity).Append('/').Append(p.Status).Append(' ');
                }

                sb.Append("warn=").Append(r.GlobalWarnings.Count);
                return sb.ToString();
            };

            string origin = run(0, 0);
            // Khung: SL 3 tren vat lieu + SL 2 gan mep lo -> hai SL khac nhau -> mo ho (tam 1).
            // Chi tiet nho: SL 4. Chi tiet xa: "SL: abc" -> mo ho. Chu cach 3e6 mm -> 1 canh bao chung.
            Equal("0,1:1/Ambiguous 120,0:4/Warning 5000,0:1/Ambiguous warn=1", origin, "goc 0");
            foreach (double[] o in new[] { new[] { 1e6, 1e6 }, new[] { -1e6, 2e6 }, new[] { -3e6, -3e6 }, new[] { 1e7, -1e7 } })
            {
                Equal(origin, run(o[0], o[1]), "goc (" + o[0] + ", " + o[1] + ") phai giong goc 0");
            }
        }

        private static void R45_AdversarialMetadata()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            ExpectReading(p, "SL\uFF1A2", 2, null, "");                       // dau hai cham full-width
            ExpectReading(p, "SL\uFF1Aabc", 0, null, "Q");
            ExpectReading(p, "0MM", 0, null, "D");                             // do day 0 -> khong phai vat lieu
            ExpectReading(p, "0.0MM", 0, null, "D");
            ExpectReading(p, "1.2MMX", 0, null, "");                           // duoi la -> khong phai thong tin
            ExpectReading(p, "1.2 MMS", 0, null, "");
            ExpectReading(p, "  1.5   mm  ", 0, "1.5MM", "");
            ExpectReading(p, "SL: 3 SL: abc", 3, null, "Q");                   // mot dung + mot sai
            ExpectReading(p, "sl:0003,1.2mm", 3, "1.2MM", "");

            // Sat bien 0.001 mm: trong / ngoai deu gan dung chi tiet do (khong co chi tiet thu hai).
            RecognizedPart inner = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 4", 199.999, 50)))[0];
            Equal(4, inner.Quantity, "trong bien 0.001 mm");
            RecognizedPart outer = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 4", 200.001, 50)))[0];
            Equal(4, outer.Quantity, "ngoai bien 0.001 mm (gan nhat)");

            // Hai chu CHONG dung mot cho, gia tri khac nhau -> mo ho, khong chon bua.
            RecognizedPart stacked = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL: 2", 100, 50), Text("SL: 5", 100, 50)))[0];
            Equal(PartStatus.Ambiguous, stacked.Status, "hai chu chong cho, SL khac nhau");

            // Chu full-width SL trong chi tiet: la THONG TIN, khong phai chu cat.
            RecognizedPart fw = Valid(Recognize(new List<CurveChain> { Box(0, 0, 200, 100) }, Text("SL\uFF1A7", 100, 50)))[0];
            Equal(7, fw.Quantity, "SL full-width");
            Equal(0, fw.EngravingSources.Count, "khong thanh chu cat");
        }

        private static void R13_ConflictingQuantities()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 2", 50, 50), Text("SL: 4", 50, 20));
            Equal(PartStatus.Ambiguous, Valid(r)[0].Status, "two SL values");
        }

        private static void R14_TextTooFar()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 7", 5000, 5000));
            Equal(1, r.GlobalWarnings.Count, "warning for far text");
            Equal(1, Valid(r)[0].Quantity, "not assigned");
        }

        private static void R15_BranchingInvalid()
        {
            // A square whose outline lines meet a third line at the same vertex (T-junction at node).
            List<CurveChain> c = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 0, 100, 100),
                Chain(false, 100, 100, 0, 100),
                Chain(false, 0, 100, 0, 0),
                Chain(false, 100, 0, 300, 0),
                Chain(false, 300, 0, 300, 100),
                Chain(false, 300, 100, 100, 100)
            };
            RecognitionResult r = Recognize(c);

            // Nguoi dung yeu cau: net gap nhau 1 diem thi GOP het vao MOT chi tiet, ghep theo
            // duong bao ngoai cung (300 x 100), va van ghi chu cho biet.
            Equal(1, r.Parts.Count, "mot chi tiet: " + string.Join(" | ", r.Parts.ConvertAll(p => p.Status + " " + p.NotesText).ToArray()));
            RecognizedPart part = r.Parts[0];
            True(part.IsNestable, "ghep duoc: " + part.NotesText);
            True(part.NotesText.Contains("re nhanh"), "van ghi chu la re nhanh: " + part.NotesText);
            True(Math.Abs(part.Outer.Area - 300 * 100) < 1e-3, "duong bao ngoai cung 300x100, dang co " + part.Outer.Area);
            Equal(7, part.GeometrySources.Count, "moi net deu xuat ra");
        }

        private static void R16_ZeroArea()
        {
            List<CurveChain> c = new List<CurveChain> { Chain(false, 0, 0, 100, 0), Chain(false, 100, 0, 0, 0) };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "one record");
            True(r.Parts[0].IsNestable, "dien tich 0 van thanh phoi (hinh chu nhat bao, day 1 mm)");
            Equal(PartStatus.Warning, r.Parts[0].Status, "chi la canh bao");
            AssertCoversChains(r.Parts[0], c);
            AssertNestsValid(r);
        }

        private static void R17b_BetweenTwoPartsResolved()
        {
            // (a) A co "SL: 3" ngay trong minh, B khong co gi; "SL: 5" nam giua -> cua B.
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(250, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 3", 100, 50), Text("SL: 5", 225, 50));
            RecognizedPart a = Valid(r).Find(p => p.MinX < 1), b = Valid(r).Find(p => p.MinX > 1);
            Equal(3, a.Quantity, "A giu SL cua minh");
            Equal(5, b.Quantity, "chu o giua ve B (B chua co SL)");
            True(a.Status != PartStatus.Ambiguous && b.Status != PartStatus.Ambiguous,
                "khong con mo ho: " + a.NotesText + " / " + b.NotesText);

            // (b) ca hai deu co SL rieng; chu thua o giua -> bo qua, khong mo ho.
            r = Recognize(c, Text("SL: 3", 100, 50), Text("SL: 4", 350, 50), Text("SL: 9", 225, 50));
            a = Valid(r).Find(p => p.MinX < 1);
            b = Valid(r).Find(p => p.MinX > 1);
            Equal(3, a.Quantity, "A giu 3");
            Equal(4, b.Quantity, "B giu 4");
            True(a.Status != PartStatus.Ambiguous && b.Status != PartStatus.Ambiguous, "chu thua khong lam mo ho");

            // (c) ca hai deu chua co -> VAN mo ho (gan bua la sai SL).
            r = Recognize(c, Text("SL: 5", 225, 50));
            True(Valid(r).TrueForAll(p => p.Status == PartStatus.Ambiguous), "khong biet cua ai thi van phai hoi");
        }

        private static void R17_UnconfirmedAmbiguousBlocked()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(260, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 230, 50));
            bool threw = false;
            try
            {
                PartRecognizer.ToPartGroups(r.Parts, 0.05);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            True(threw, "unconfirmed ambiguous record must block nesting");

            foreach (RecognizedPart p in r.Parts) p.Confirmed = true;
            Equal(2, PartRecognizer.ToPartGroups(r.Parts, 0.05).Count, "after confirmation");
        }
    }
}
