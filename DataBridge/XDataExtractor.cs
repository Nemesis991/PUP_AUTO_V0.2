using Autodesk.AutoCAD.DatabaseServices;

namespace PUP_AUTO.DataBridge
{
    public static class XDataExtractor
    {
        public static string GetParcelId(Entity ent, string regAppName = "TransCAD")
        {
            try
            {
                using (ResultBuffer resBuf = ent.GetXDataForApplication(regAppName))
                {
                    if (resBuf == null)
                    {
                        return "Неизвестен_Имот";
                    }

                    TypedValue[] xdata = resBuf.AsArray();

                    if (xdata.Length >= 2 && xdata[1].Value is string)
                    {
                        return xdata[1].Value.ToString();
                    }

                    return "Неизвестен_Имот";
                }
            }
            catch (Exception)
            {
                return "Грешка_XData";
            }
        }
    }
}
