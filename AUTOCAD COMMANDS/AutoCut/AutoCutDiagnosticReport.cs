using System;
using System.Collections.Generic;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    public class AutoCutDiagnosticReport
    {
        public int TargetsCount { get; set; }
        public int TotalCandidatesInSpace { get; set; }
        public int RejectedByType { get; set; }
        public int RejectedByTargetSelf { get; set; }
        public int RejectedByOwner { get; set; }
        public int RejectedByLayer { get; set; }
        public int RejectedByColor { get; set; }
        public int RejectedByBoundingBox { get; set; }
        public int EvaluatedGeometricIntersection { get; set; }
        public int RejectedByNoIntersection { get; set; }
        public int ValidCuttersCount { get; set; }
        public int IntersectionsCount { get; set; }
        public int DuplicateIntersectionsCount { get; set; }
        public int SegmentsToRemoveCount { get; set; }
        public int SegmentsToKeepCount { get; set; }
        public int ObjectsToCreateCount => SegmentsToKeepCount;
        public int ObjectsToEraseCount => TargetsWithCutsCount;
        public int TargetsWithCutsCount { get; set; }
        public int SkippedSingleIntersectionCount { get; set; }

        public List<string> DetailLogs { get; } = new List<string>();

        public string FormatSummaryText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Targets được chọn: {TargetsCount}");
            sb.AppendLine($"Candidates quét được trong Space: {TotalCandidatesInSpace}");
            sb.AppendLine($"Cutters hợp lệ (Valid cutters): {ValidCuttersCount}");
            sb.AppendLine($"Giao điểm hợp lệ (Intersections): {IntersectionsCount}");
            sb.AppendLine($"Đoạn sẽ CẮT BỎ (REMOVE): {SegmentsToRemoveCount}");
            sb.AppendLine($"Đoạn sẽ GIỮ LẠI (KEEP): {SegmentsToKeepCount}");
            sb.AppendLine($"Đối tượng mới sẽ tạo: {ObjectsToCreateCount}");
            sb.AppendLine($"Đối tượng gốc sẽ xóa: {ObjectsToEraseCount}");
            return sb.ToString();
        }

        public string FormatDiagnosticLog()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== BÁO CÁO CHẨN ĐOÁN CHI TIẾT (DIAGNOSTICS) ===");
            sb.AppendLine($"Target count: {TargetsCount}");
            sb.AppendLine($"Candidates in space: {TotalCandidatesInSpace}");
            sb.AppendLine($"Rejected by type: {RejectedByType}");
            sb.AppendLine($"Rejected by layer: {RejectedByLayer}");
            sb.AppendLine($"Rejected by color: {RejectedByColor}");
            sb.AppendLine($"Rejected by bounding box (không gần target): {RejectedByBoundingBox}");
            sb.AppendLine($"Evaluated geometric intersection: {EvaluatedGeometricIntersection}");
            sb.AppendLine($"Rejected by no geometric intersection: {RejectedByNoIntersection}");
            sb.AppendLine($"Valid cutters: {ValidCuttersCount}");
            sb.AppendLine($"Total intersections found: {IntersectionsCount + DuplicateIntersectionsCount}");
            sb.AppendLine($"Deduplicated identical points: {DuplicateIntersectionsCount}");
            sb.AppendLine($"Valid unique intersections: {IntersectionsCount}");
            if (SkippedSingleIntersectionCount > 0)
            {
                sb.AppendLine($"Skipped because Single Intersection = Skip: {SkippedSingleIntersectionCount} target(s)");
            }
            sb.AppendLine($"Segments to remove: {SegmentsToRemoveCount}");
            sb.AppendLine($"Segments to keep: {SegmentsToKeepCount}");
            sb.AppendLine();
            sb.AppendLine("--- Chi tiết từng Target ---");
            foreach (string log in DetailLogs)
            {
                sb.AppendLine(log);
            }
            return sb.ToString();
        }
    }
}

