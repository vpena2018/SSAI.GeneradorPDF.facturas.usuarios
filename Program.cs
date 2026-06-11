using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SSAI.GeneradorPDF.facturas.usuarios
{
    class Program
    {
        static Mutex mutex = null;
        static async Task Main(string[] args)
        {
            bool createdNew;

            mutex = new Mutex(
                true,
                "SSAI_GENERADOR_FACTURAS_MUTEX",
                out createdNew
            );

            // YA HAY OTRA INSTANCIA
            if (!createdNew)
            {
                return;
            }

            try
            {

                var facturas =
                    await Logic.exportacion
                        .ObtenerFacturasParaGenerarPDF(
                            DateTime.Now.Date,
                            DateTime.Now.Date
                        );

                var inforcorrelativos = new Models.Hertz_ProjectsEntities().correlatives.ToList();

                if (facturas.Count <= 0 || inforcorrelativos.Count <= 0)
                {
                    return;
                }

                // COPIAS
                await ejecutarExportacionCopias(facturas, inforcorrelativos);

                // REINTENTO FALTANTES
                //VerificarYRegenerarFacturasFaltantes(facturas, inforcorrelativos);
            }
            finally
            {
                mutex.Dispose();
            }
        }


        private static async Task ejecutarExportacionCopias(List<Logic.exportacion.facturaEnvioRow> facturas, List<Models.correlatives> infocorrelativo)
        {
            //string carpetaTemp =
            //    @"\\10.10.1.31\scaneos\SAP\facturas_pdf_SAP\temp";

            string carpetaTemp = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "temp_pdf"
            );

            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Thread.Sleep(1000);

                if (Directory.Exists(carpetaTemp))
                {
                    Directory.Delete(carpetaTemp, true);
                }
            }
            catch
            {
            }


            try
            {

                // CREAR TEMP
                if (!Directory.Exists(carpetaTemp))
                {
                    Directory.CreateDirectory(carpetaTemp);
                }

                var usuarios = new Models.Hertz_ProjectsEntities()
                    .ssai_users
                    .ToList();

                foreach (var factura in facturas)
                {
                    try
                    {

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();

                        Thread.Sleep(1000);


                        var rowusuario = usuarios
                            .FirstOrDefault(x =>
                                x.user_SSAI == factura.usuario);

                        byte[] firma = null;

                        if (rowusuario != null)
                        {
                            firma = rowusuario.firma;
                        }

                        var numeroFactura = factura.n_factura
                            .Replace("-", "")
                            .Trim();

                        string carpetaCopia = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "firmas_copias"
                        );

                        if (!Directory.Exists(carpetaCopia))
                            Directory.CreateDirectory(carpetaCopia);

                        //string rutaFirma = Path.Combine(
                        //    carpetaCopia,
                        //    $"firma_{numeroFactura}.png"
                        //);

                        string rutaFirma = Path.Combine(
                            carpetaCopia,
                            $"firma_{numeroFactura}_{Guid.NewGuid()}.png"
                        );

                        List<string> rutasPdfGenerados =
                            new List<string>();

                        string error = "Error generando PDF";

                        string contratoFinal = ObtenerContratoRentworks(
                        factura.contrato,
                        infocorrelativo);



                        bool pdfCopiasOk =
                            Logic.exportacion.generatePdfFacturaCopias(
                                factura,
                                factura.docEntry,
                                numeroFactura,
                                firma,
                                rutaFirma,
                                out rutasPdfGenerados,
                                out error
                            );

                        if (pdfCopiasOk)
                        {
                            foreach (var rutaPdf in rutasPdfGenerados)
                            {
                                bool archivoLibre =
                                    Logic.exportacion.EsperarArchivoLibre(
                                        rutaPdf
                                    );

                                if (!archivoLibre)
                                {
                                    pdfCopiasOk = false;

                                    error =
                                        $"Archivo bloqueado: {rutaPdf}";

                                    break;
                                }
                            }

                            // MERGE
                            if (pdfCopiasOk)
                            {

                                string carpetaFacturas = ObtenerRutaFacturas(factura.contrato);

                                Directory.CreateDirectory(carpetaFacturas);

                                string rutaPdfFinal = Path.Combine(
                                    carpetaFacturas,
                                    $"{contratoFinal}.pdf"
                                );

                                Logic.exportacion.UnirPdfs(
                                    rutasPdfGenerados,
                                    rutaPdfFinal
                                );

                                pdfCopiasOk =
                                    Logic.exportacion.EsperarArchivoLibre(
                                        rutaPdfFinal
                                    );

                                await Logic.exportacion.GuardarFacturaPdfGenerado(
                                    factura.InvoiceId,
                                    factura.docEntry,
                                    factura.contrato,
                                    factura.n_factura,
                                    pdfCopiasOk,
                                    factura.usuario,
                                    pdfCopiasOk
                                        ? rutaPdfFinal
                                        : null,
                                    pdfCopiasOk ? null : error
                                );
                            }
                        }

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();

                        //Thread.Sleep(2000);
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(
                            Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory,
                                "errores_copias.txt"
                            ),
                            $"{DateTime.Now} - {ex}\r\n\r\n"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "errores_copias_general.txt"
                    ),
                    $"{DateTime.Now} - {ex}\r\n\r\n"
                );
            }
            finally
            {

            }
        }

        private static void VerificarYRegenerarFacturasFaltantes(
List<Logic.exportacion.facturaEnvioRow> facturas, List<Models.correlatives> infocorrelativo)
        {
            try
            {
                string carpetaFinal =
                    @"\\10.10.1.31\scaneos\SAP\facturas_pdf_SAP";

                var usuarios = new Models.Hertz_ProjectsEntities()
                    .ssai_users
                    .ToList();

                // BUSCAR FALTANTES
                var facturasFaltantes = facturas
                    .Where(f =>
                    {
                        string rutaPdfFinal = Path.Combine(
                            carpetaFinal,
                            $"Factura_{f.contrato}.pdf"
                        );

                        return !File.Exists(rutaPdfFinal);
                    })
                    .ToList();

                // SI NO HAY FALTANTES
                if (facturasFaltantes.Count <= 0)
                {
                    return;
                }

                // REGENERAR
                foreach (var factura in facturasFaltantes)
                {
                    try
                    {
                        var rowusuario = usuarios
                            .FirstOrDefault(x =>
                                x.user_SSAI == factura.usuario);

                        byte[] firma = null;

                        if (rowusuario != null)
                        {
                            firma = rowusuario.firma;
                        }

                        var numeroFactura = factura.n_factura
                            .Replace("-", "")
                            .Trim();

                        string carpetaCopia = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "firmas_copias"
                        );

                        if (!Directory.Exists(carpetaCopia))
                        {
                            Directory.CreateDirectory(carpetaCopia);
                        }

                        //string rutaFirma = Path.Combine(
                        //    carpetaCopia,
                        //    $"firma_retry_{numeroFactura}.png"
                        //);

                        string rutaFirma = Path.Combine(
                            carpetaCopia,
                            $"firma_retry_{numeroFactura}_{Guid.NewGuid()}.png"
                        );

                        List<string> rutasPdfGenerados =
                            new List<string>();

                        string error = string.Empty;

                        bool pdfCopiasOk =
                            Logic.exportacion.generatePdfFacturaCopias(
                                factura,
                                factura.docEntry,
                                numeroFactura,
                                firma,
                                rutaFirma,
                                out rutasPdfGenerados,
                                out error
                            );

                        if (!pdfCopiasOk)
                        {
                            File.AppendAllText(
                                Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    "errores_retry.txt"
                                ),
                                $"{DateTime.Now} - ERROR GENERANDO {factura.contrato} - {error}\r\n"
                            );

                            continue;
                        }

                        // VALIDAR TEMPORALES
                        bool todosExisten = true;

                        foreach (var rutaPdf in rutasPdfGenerados)
                        {
                            int intentos = 0;

                            while (!File.Exists(rutaPdf) &&
                                   intentos < 10)
                            {
                                Thread.Sleep(300);
                                intentos++;
                            }

                            if (!File.Exists(rutaPdf))
                            {
                                todosExisten = false;

                                File.AppendAllText(
                                    Path.Combine(
                                        AppDomain.CurrentDomain.BaseDirectory,
                                        "errores_retry.txt"
                                    ),
                                    $"{DateTime.Now} - TEMP NO EXISTE {rutaPdf}\r\n"
                                );

                                break;
                            }
                        }

                        if (!todosExisten)
                        {
                            continue;
                        }

                        // MERGE FINAL
                        string rutaPdfFinal = Path.Combine(
                            carpetaFinal,
                            $"Factura_{factura.contrato}.pdf"
                        );

                        // SI EXISTE BORRAR
                        try
                        {
                            if (File.Exists(rutaPdfFinal))
                            {
                                File.Delete(rutaPdfFinal);
                            }
                        }
                        catch
                        {
                        }

                        Logic.exportacion.UnirPdfs(
                            rutasPdfGenerados,
                            rutaPdfFinal
                        );

                        // VALIDAR FINAL
                        int intentosFinal = 0;

                        while (!File.Exists(rutaPdfFinal) &&
                               intentosFinal < 10)
                        {
                            Thread.Sleep(500);
                            intentosFinal++;
                        }

                        if (!File.Exists(rutaPdfFinal))
                        {
                            File.AppendAllText(
                                Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    "errores_retry.txt"
                                ),
                                $"{DateTime.Now} - FINAL NO EXISTE {factura.contrato}\r\n"
                            );
                        }
                        else
                        {
                            File.AppendAllText(
                                Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    "errores_retry.txt"
                                ),
                                $"{DateTime.Now} - RECUPERADA {factura.contrato}\r\n"
                            );
                        }

                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(
                            Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory,
                                "errores_retry.txt"
                            ),
                            $"{DateTime.Now} - {factura.contrato} - {ex}\r\n\r\n"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "errores_retry_general.txt"
                    ),
                    $"{DateTime.Now} - {ex}\r\n\r\n"
                );
            }
        }
        private static string ObtenerContratoRentworks(
    string contrato,
    List<Models.correlatives> correlativos)
        {
            if (string.IsNullOrWhiteSpace(contrato))
                return contrato;

            string[] partes = contrato.Split('_');

            if (partes.Length < 2)
                return contrato;

            string costCenter = partes[0];

            var correlativo = correlativos.FirstOrDefault(x =>
                x.cost_center.Equals(
                    costCenter,
                    StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(correlativo?.rentworks_code))
                return contrato;

            string rentworksCode = correlativo.rentworks_code;

            // NORMAL: OPT_321789
            if (partes.Length == 2)
            {
                return $"Factura_{rentworksCode}{partes[1]}";
            }

            // AVERÍA: OPT_AVE_38743
            if (partes.Length == 3 &&
                partes[1].Equals("AVE", StringComparison.OrdinalIgnoreCase))
            {
                return $"Factura_AVE_{rentworksCode}{partes[2]}";
            }

            // ADICIÓN: OPT_321812_1
            if (partes.Length == 3)
            {
                return $"Factura_{rentworksCode}{partes[1]}_{partes[2]}";
            }

            return contrato;
        }

        private static string ObtenerRutaFacturas(string contrato)
        {
            if (string.IsNullOrWhiteSpace(contrato))
                throw new ArgumentException("Contrato inválido");

            string[] partes = contrato.Split('_');

            if (partes.Length < 2)
                throw new ArgumentException(
                    $"Formato de contrato inválido: {contrato}");

            string costCenter = partes[0];

            using (var db = new Models.Hertz_ProjectsEntities())
            {
                string ubicacion = db.locations
                    .Where(x => x.code == costCenter)
                    .Select(x => x.ubicacion)
                    .FirstOrDefault();

                switch (ubicacion?.Trim().ToUpper())
                {
                    case "SPS":
                        return @"\\10.10.1.31\ca-sps\facturas";

                    case "TGU":
                        return @"\\10.10.1.31\ca-tgu\facturas";

                    default:
                        throw new Exception(
                            $"No se encontró ubicación para el cost center '{costCenter}'");
                }
            }
        }


    }
}
