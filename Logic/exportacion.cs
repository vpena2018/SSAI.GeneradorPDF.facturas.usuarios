using CrystalDecisions.CrystalReports.Engine;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SSAI.GeneradorPDF.facturas.usuarios.Logic
{
    class exportacion
    {

        private static string connSSAI = ConfigurationManager.ConnectionStrings["Hertz_ProjectsEntities"].ToString().Split('"')[1];

        public class clienteInfoCorreo
        {
            public string CardCode { get; set; }
            public string CardName { get; set; }
            public string Email { get; set; }

        }

        public class facturaEnvioRow
        {
            public int docEntry { get; set; }
            public int InvoiceId { get; set; }
            public string contrato { get; set; }
            public string franquicia { get; set; }
            public string n_factura { get; set; }
            public string usuario { get; set; }
            public string CardCode { get; set; }
            public string CardName { get; set; }
            public string correo { get; set; }
            public int docnum { get; set; }
            public DateTime docDate { get; set; }
            public decimal granTotal { get; set; }
            public int cantidad_lineas { get; set; }
            public string estado_envio { get; set; }
            public DateTime? ultima_fecha_envio { get; set; }
        }

        public static bool EsperarArchivoLibre(
            string ruta,
            int intentos = 10)
        {
            for (int i = 0; i < intentos; i++)
            {
                try
                {
                    using (FileStream fs = File.Open(
                        ruta,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                    {
                        return true;
                    }
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }

            return false;
        }

        public static void UnirPdfs(
            List<string> archivosPdf,
            string rutaSalida)
        {
            using (Document document = new Document())
            {
                using (FileStream stream = new FileStream(
                    rutaSalida,
                    FileMode.Create))
                {
                    using (PdfCopy copy = new PdfCopy(document, stream))
                    {
                        document.Open();

                        foreach (string archivo in archivosPdf)
                        {
                            using (PdfReader reader = new PdfReader(archivo))
                            {
                                for (int i = 1; i <= reader.NumberOfPages; i++)
                                {
                                    copy.AddPage(
                                        copy.GetImportedPage(reader, i)
                                    );
                                }
                            }
                        }
                    }
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public static bool generatePdfFacturaCopias(
    facturaEnvioRow factura,
    int docEntry,
    string n_factura,
    byte[] firma,
    string rutaFirma,
    out List<string> rutasPdfGenerados,
    out string error)
        {
            ReportDocument cryRpt = new ReportDocument();

            rutasPdfGenerados = new List<string>();
            error = string.Empty;

            try
            {
                var db = "SBO_HERTZ_PRUEBAS";

                using (var context = new Models.Hertz_ProjectsEntities())
                {
                    var row = context.api_configuration.FirstOrDefault();

                    if (row != null)
                        db = row.CompanyDB;
                }

                var reporte = string.Empty;

                if (factura.franquicia.ToUpper() == "HERTZ")
                {
                    reporte = "FacturadeVentaHERTZ_correo.rpt";
                }
                else if (factura.franquicia.ToUpper() == "DOLLAR")
                {
                    reporte = "FacturadeVentaDOLLAR_correo.rpt";
                }
                else if (factura.franquicia.ToUpper() == "THRIFTY")
                {
                    reporte = "FacturadeVentaTHRIFTY_correo.rpt";
                }
                else
                {
                    return false;
                }

                var rutaReporte = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "reportes",
                    reporte
                );

                // LIMPIAR FACTURA
                string facturaLimpia = n_factura
                    .Replace("/", "_")
                    .Replace("\\", "_")
                    .Replace(" ", "_");

                // NUEVA RUTA

                //string carpetaShare =
                //@"\\10.10.1.31\scaneos\SAP\facturas_pdf_SAP\temp";

                string carpetaShare = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "temp_pdf"
                );

                // CREAR CARPETA SI NO EXISTE
                if (!Directory.Exists(carpetaShare))
                    Directory.CreateDirectory(carpetaShare);

                // CARGAR REPORTE
                cryRpt.Load(rutaReporte);

                // CONEXION
                cryRpt.DataSourceConnections[0].SetConnection(
                    "10.10.2.10",
                    db,
                    "System",
                    "Sap5erver"
                );

                // PARAMETROS
                cryRpt.SetParameterValue("UserCode@", "dvelasquez");
                cryRpt.SetParameterValue("Schema@", db);
                cryRpt.SetParameterValue("DocKey@", docEntry);



                // FIRMA
                if (firma != null && firma.Length > 0)
                {
                    string carpetaFirma = Path.GetDirectoryName(rutaFirma);

                    if (!Directory.Exists(carpetaFirma))
                        Directory.CreateDirectory(carpetaFirma);

                    File.WriteAllBytes(rutaFirma, firma);

                    cryRpt.SetParameterValue("FirmaRuta", rutaFirma);
                }

                // COPIAS
                List<string> archivos = new List<string>()
        {
            "ORIGINAL",
            "CONTABILIDAD"
        };

                foreach (var departamento in archivos)
                {
                    // LOG INICIO
                    File.AppendAllText(
                        Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "debug_exports.txt"
                        ),
                        $"{DateTime.Now} - INICIO - " +
                        $"{factura.contrato} - " +
                        $"{departamento}\r\n"
                    );

                    cryRpt.SetParameterValue(
                        "TipoCopia",
                        departamento
                    );

                    // NOMBRE PDF
                    string nombrePdf =
                        $"Factura_{factura.contrato}_{departamento}.pdf";




                    // RUTA FINAL
                    string rutaPdfFinal = Path.Combine(
                        carpetaShare,
                        nombrePdf
                    );

                    // EXPORTAR
                    cryRpt.ExportToDisk(
                        CrystalDecisions.Shared
                            .ExportFormatType
                            .PortableDocFormat,
                        rutaPdfFinal
                    );

                    // LOG EXPORT OK
                    File.AppendAllText(
                        Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "debug_exports.txt"
                        ),
                        $"{DateTime.Now} - EXPORT OK - " +
                        $"{factura.contrato} - " +
                        $"{departamento}\r\n"
                    );

                    // VALIDAR EXISTE
                    if (!File.Exists(rutaPdfFinal))
                    {
                        File.AppendAllText(
                            Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory,
                                "debug_exports.txt"
                            ),
                            $"{DateTime.Now} - NO EXISTE - " +
                            $"{rutaPdfFinal}\r\n"
                        );
                    }
                    else
                    {
                        File.AppendAllText(
                            Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory,
                                "debug_exports.txt"
                            ),
                            $"{DateTime.Now} - EXISTE - " +
                            $"{rutaPdfFinal}\r\n"
                        );
                    }

                    rutasPdfGenerados.Add(rutaPdfFinal);

                    // ESPERA
                    Thread.Sleep(1000);
                }

                return true;
            }
            catch (Exception ex)
            {
                rutasPdfGenerados = new List<string>();
                error = ex.Message;

                return false;
            }
            finally
            {
                try
                {
                    cryRpt.Close();
                    cryRpt.Dispose();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                // BORRAR FIRMA TEMPORAL
                try
                {
                    if (!string.IsNullOrWhiteSpace(rutaFirma) &&
                        File.Exists(rutaFirma))
                    {
                        File.Delete(rutaFirma);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        public static async Task<List<facturaEnvioRow>> ObtenerFacturasParaGenerarPDF(
DateTime? fechaInicio,
DateTime? fechaFin,
string cliente = null)
        {
            var res = new List<facturaEnvioRow>();

            try
            {
                using (var connSec = new SqlConnection(connSSAI))
                {
                    await connSec.OpenAsync();

                    using (var sqlc = new SqlCommand(
                        "OBTENER_FACTURAS_PARA_GENERAR_PDF_USUARIOS",
                        connSec))
                    {
                        sqlc.CommandType = CommandType.StoredProcedure;

                        sqlc.Parameters.AddWithValue(
                            "@fechaInicio",
                            (object)fechaInicio ?? DBNull.Value
                        );

                        sqlc.Parameters.AddWithValue(
                            "@fechaFin",
                            (object)fechaFin ?? DBNull.Value
                        );

                        sqlc.Parameters.AddWithValue(
                            "@cliente",
                            string.IsNullOrWhiteSpace(cliente)
                                ? (object)DBNull.Value
                                : cliente
                        );

                        using (var reader = await sqlc.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new facturaEnvioRow();

                                row.usuario = reader["user_SSAI"]?.ToString();

                                row.franquicia = reader["franquicia"]?.ToString();

                                row.contrato = reader["U_CONTRATO"]?.ToString();

                                row.n_factura = reader["n_factura"]?.ToString();

                                row.correo = reader["U_EMAIL"]?.ToString();

                                row.docEntry = reader["docEntry"] != DBNull.Value
                                    ? Convert.ToInt32(reader["docEntry"])
                                    : 0;

                                row.InvoiceId = reader["InvoiceId"] != DBNull.Value
                                    ? Convert.ToInt32(reader["InvoiceId"])
                                    : 0;

                                row.CardCode = reader["CardCode"]?.ToString();

                                row.CardName = reader["CardName"]?.ToString();

                                row.docnum = reader["docnum"] != DBNull.Value
                                    ? Convert.ToInt32(reader["docnum"])
                                    : 0;

                                row.docDate = reader["docDate"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["docDate"])
                                    : DateTime.MinValue;

                                row.granTotal = reader["granTotal"] != DBNull.Value
                                    ? Convert.ToDecimal(reader["granTotal"])
                                    : 0;

                                row.cantidad_lineas =
                                    reader["cantidad_lineas"] != DBNull.Value
                                        ? Convert.ToInt32(
                                            reader["cantidad_lineas"])
                                        : 0;

                                row.estado_envio =
                                    reader["estado_envio"]?.ToString();

                                row.ultima_fecha_envio =
                                    reader["ultima_fecha_envio"] != DBNull.Value
                                        ? (DateTime?)Convert.ToDateTime(
                                            reader["ultima_fecha_envio"])
                                        : null;

                                res.Add(row);
                            }
                        }
                    }
                }

                var clientes = ObtenerclientesParaEnvioCorreo();

                res = CompletarCorreosFacturas(res, clientes);

                return res;
            }
            catch
            {
                return res;
            }
        }


        public static List<clienteInfoCorreo> ObtenerclientesParaEnvioCorreo()
        {
            var res = new List<clienteInfoCorreo>();

            try
            {
                using (var connSec = new SqlConnection(connSSAI))
                {
                    if (connSec.State == ConnectionState.Open)
                        connSec.Close();
                    connSec.Open();
                    var dt = new DataTable();
                    var comm = "BUSCAR_CLIENTES_ENVIO_ESTADO_CTA";
                    var sqlc = new SqlCommand(comm, connSec);
                    sqlc.CommandType = CommandType.StoredProcedure;
                    var da = new SqlDataAdapter(sqlc);
                    da.Fill(dt);
                    connSec.Close();

                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        clienteInfoCorreo row = new clienteInfoCorreo();

                        row.CardCode = Convert.ToString(dt.Rows[i]["CardCode"]);
                        row.CardName = Convert.ToString(dt.Rows[i]["CardName"]);

                        row.Email = Convert.ToString(dt.Rows[i]["U_EMAIL_ECUENTA"])
                        .Replace("\"", "")  // Elimina las comillas dobles
                        .Replace(";", ",");

                        res.Add(row);

                    }

                    return res;
                }
            }
            catch (Exception ex)
            {
                //model.Log.Writelog("GetDraftByParm", ex.Message, ex.StackTrace, "", "facturacion SSAI");
                return res;
            }

        }

        public static List<facturaEnvioRow> CompletarCorreosFacturas(
List<facturaEnvioRow> facturas,
List<clienteInfoCorreo> clientes)
        {
            try
            {
                if (facturas == null || clientes == null)
                    return facturas;

                // 🔥 diccionario para búsqueda rápida
                var clientesDict = clientes
                    .GroupBy(x => x.CardCode)
                    .ToDictionary(x => x.Key, x => x.First().Email);

                foreach (var factura in facturas)
                {
                    // 🔥 si ya tiene correo desde SAP
                    if (!string.IsNullOrWhiteSpace(factura.correo))
                        continue;

                    // 🔥 buscar correo por CardCode
                    if (clientesDict.ContainsKey(factura.CardCode))
                    {
                        factura.correo = clientesDict[factura.CardCode];
                    }
                }

                return facturas;
            }
            catch (Exception ex)
            {

                return facturas;
            }
        }

        public static async Task<bool> GuardarFacturaPdfGenerado(
int invoiceId,
int docentry,
string contrato,
string n_factura,
bool generado,
string usuario,
string rutaPdf = null,
string errorPdf = null)
        {
            try
            {
                using (var connSec = new SqlConnection(connSSAI))
                {
                    await connSec.OpenAsync();

                    using (var sqlc = new SqlCommand(
                        "GUARDAR_FACTURA_PDF_GENERADO_USUARIOS",
                        connSec))
                    {
                        sqlc.CommandType = CommandType.StoredProcedure;

                        sqlc.Parameters.AddWithValue(
                            "@InvoiceId",
                            invoiceId
                        );

                        sqlc.Parameters.AddWithValue(
                            "@doc_entry",
                            docentry
                        );

                        sqlc.Parameters.AddWithValue(
                            "@contrato",
                            string.IsNullOrWhiteSpace(contrato)
                                ? (object)DBNull.Value
                                : contrato
                        );

                        sqlc.Parameters.AddWithValue(
                            "@generado",
                            generado
                        );

                        sqlc.Parameters.AddWithValue(
                            "@n_factura",
                            string.IsNullOrWhiteSpace(n_factura)
                                ? (object)DBNull.Value
                                : n_factura
                        );

                        sqlc.Parameters.AddWithValue(
                            "@usuario_factura",
                            string.IsNullOrWhiteSpace(usuario)
                                ? (object)DBNull.Value
                                : usuario
                        );



                        sqlc.Parameters.AddWithValue(
                            "@ruta_pdf",
                            string.IsNullOrWhiteSpace(rutaPdf)
                                ? (object)DBNull.Value
                                : rutaPdf
                        );

                        sqlc.Parameters.AddWithValue(
                            "@error_pdf",
                            string.IsNullOrWhiteSpace(errorPdf)
                                ? (object)DBNull.Value
                                : errorPdf
                        );

                        await sqlc.ExecuteNonQueryAsync();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
