using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Kiem tra tham so dau vao TRUOC khi cham vao hinh hoc.
    /// Nguyen tac: khong bao gio im lang tao ra hinh hoc sai - moi van de deu phai co thong bao.
    /// </summary>
    public static class FoilValidation
    {
        public static List<string> ValidateSettings(FoilSettings settings)
        {
            List<string> errors = new List<string>();
            CultureInfo ci = CultureInfo.InvariantCulture;

            if (settings == null)
            {
                errors.Add("Thieu cau hinh.");
                return errors;
            }

            if (!IsFinite(settings.BlankLength) || settings.BlankLength <= 0.0)
            {
                errors.Add("Chieu dai phoi phai la so duong.");
            }

            if (!IsFinite(settings.Thickness) || settings.Thickness <= 0.0)
            {
                errors.Add("Chieu day ton T phai la so duong.");
            }

            if (!IsFinite(settings.InsideRadius) || settings.InsideRadius < 0.0)
            {
                errors.Add("Ban kinh chan trong R phai >= 0.");
            }

            switch (settings.Method)
            {
                case FoilBendMethod.BendDeductionKFactor:
                case FoilBendMethod.BendAllowanceKFactor:
                    if (!IsFinite(settings.KFactor) || settings.KFactor < 0.0 || settings.KFactor > 1.0)
                    {
                        errors.Add("K-Factor phai nam trong khoang 0 .. 1 (thuc te thuong 0.30 .. 0.50).");
                    }

                    break;

                case FoilBendMethod.CustomShopRule:
                    if (!IsFinite(settings.CustomFactor) || settings.CustomFactor < 0.0)
                    {
                        errors.Add("He so bu (Factor) phai >= 0.");
                    }

                    break;

                case FoilBendMethod.BendTable:
                    // File bang chan duoc kiem tra khi nap.
                    break;
            }

            if (!IsFinite(settings.MinBendAngleDeg) ||
                settings.MinBendAngleDeg < 0.0 ||
                settings.MinBendAngleDeg >= 90.0)
            {
                errors.Add("Nguong goc chan nho nhat phai nam trong khoang 0 .. 90 do.");
            }

            if (!IsFinite(settings.MaxBendAngleDeg) ||
                settings.MaxBendAngleDeg <= settings.MinBendAngleDeg ||
                settings.MaxBendAngleDeg >= 180.0)
            {
                errors.Add(string.Format(
                    ci,
                    "Nguong goc chan lon nhat phai lon hon {0:0.##} do va nho hon 180 do " +
                    "(OSSB phan ky khi goc chan tien toi 180 do).",
                    settings.MinBendAngleDeg));
            }

            if (settings.DuplicatePointTolerance <= 0.0)
            {
                errors.Add("Dung sai diem trung phai lon hon 0.");
            }

            if (string.IsNullOrWhiteSpace(settings.BendLayerName))
            {
                errors.Add("Ten layer duong chan khong duoc de trong.");
            }

            if (settings.BendUpColorMode == FoilColorMode.AciIndex &&
                (settings.BendUpColorIndex < 0 || settings.BendUpColorIndex > 256))
            {
                errors.Add("Ma mau ACI cho chan LEN phai nam trong khoang 0 .. 256.");
            }

            if (settings.BendDownColorMode == FoilColorMode.AciIndex &&
                (settings.BendDownColorIndex < 0 || settings.BendDownColorIndex > 256))
            {
                errors.Add("Ma mau ACI cho chan XUONG phai nam trong khoang 0 .. 256.");
            }

            return errors;
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
