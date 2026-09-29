using Autodesk.AutoCAD.DatabaseServices;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    public static class XDataExtractor
    {
        public static string GetParcelId(Entity ent, string regAppName = XDataNames.RegApp)
        {
            try
            {
                using (ResultBuffer resBuf = ent.GetXDataForApplication(regAppName))
                {
                    if (resBuf == null)
                    {
                        return XDataNames.UnknownParcel;
                    }

                    TypedValue[] xdata = resBuf.AsArray();

                    if (xdata.Length >= 2 && xdata[1].Value is string)
                    {
                        return xdata[1].Value.ToString();
                    }

                    return XDataNames.UnknownParcel;
                }
            }
            catch (Exception)
            {
                return XDataNames.XDataError;
            }
        }
    }
}
