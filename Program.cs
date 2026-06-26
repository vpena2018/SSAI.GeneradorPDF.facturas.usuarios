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
                "SSAI_GENERADOR_FACTURAS_USUARIOS_MUTEX",
                out createdNew
            );

            // YA HAY OTRA INSTANCIA
            if (!createdNew)
            {
                return;
            }

            try
            {

                DateTime desde = new DateTime(2026, 6, 25);
                DateTime hasta = DateTime.Now.Date;

                int eliminadas = await Logic.exportacion.EliminarFacturasPDFAnuladas(desde, hasta);


                var facturas =
                    await Logic.exportacion
                        .ObtenerFacturasParaGenerarPDF(
                            new DateTime(2026,6,26),
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

                        string carpetaFacturas = string.Empty;

                        string contratoFinal = ObtenerContratoRentworks(
                        factura.contrato,
                        infocorrelativo);

                        string DirectorioBasecarpetaFacturas = ObtenerRutaFacturas(factura.contrato, factura.usuario);

                        try
                        {
                            if (!Directory.Exists(DirectorioBasecarpetaFacturas))
                            {
                                File.AppendAllText(
                                    Path.Combine(
                                        AppDomain.CurrentDomain.BaseDirectory,
                                        "errores.txt"
                                    ),
                                    //$"{DateTime.Now} - {$"No se encontró carpeta {DirectorioBasecarpetaFacturas} para contrato {contratoFinal}"}\r\n\r\n"
                                    $"{DateTime.Now} - {$"No se encontró Directorio {DirectorioBasecarpetaFacturas}"}\r\n\r\n"
                                );

                                continue;
                                //Directory.CreateDirectory(DirectorioBasecarpetaFacturas);
                            }

                            carpetaFacturas = ObtenerCarpetaContrato(DirectorioBasecarpetaFacturas, contratoFinal);

                            if (string.IsNullOrEmpty(carpetaFacturas))
                            {
                                File.AppendAllText(
                                    Path.Combine(
                                        AppDomain.CurrentDomain.BaseDirectory,
                                        "errores.txt"
                                    ),
                                    $"{DateTime.Now} - {$"No se encontró carpeta en directorio {DirectorioBasecarpetaFacturas} para contrato {contratoFinal}"}\r\n\r\n"
                                );

                                continue;
                            }



                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(
                                Path.Combine(
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    "errores.txt"
                                ),
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Error: {ex}\r\n\r\n"
                            );

                            continue;
                        }



                        if (string.IsNullOrEmpty(carpetaFacturas))
                        {
                           continue;
                        }

                        //if (string.IsNullOrEmpty(carpetaFacturas))
                        //{
                        //File.AppendAllText(
                        //    Path.Combine(
                        //        AppDomain.CurrentDomain.BaseDirectory,
                        //        "errores_copias.txt"
                        //    ),
                        //    $"{DateTime.Now} - {$"No se encontró carpeta en {DirectorioBasecarpetaFacturas} para contrato {contratoFinal}"}\r\n\r\n"
                        //);

                        //    continue;
                        //}


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

                                //Directory.CreateDirectory(carpetaFacturas);

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


                                //copiar la factura en el folder donde va ubicada

                                //

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
                                "errores.txt"
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
                        "errores.txt"
                    ),
                    $"{DateTime.Now} - {ex}\r\n\r\n"
                );
            }
            finally
            {

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

        private static string ObtenerCarpetaContratoOld(
            string directorioBase,
            string contratoFinal)
        {
            string numeroContrato = contratoFinal.Replace("Factura_", "");

            string carpeta = Directory
                .GetDirectories(directorioBase)
                .FirstOrDefault(d =>
                    Path.GetFileName(d)
                        .StartsWith(
                            numeroContrato,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

            if (string.IsNullOrEmpty(carpeta))
            {
                File.AppendAllText(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "errores.txt"
                    ),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - No se encontró carpeta en {directorioBase} para contrato {contratoFinal}\r\n\r\n"
                );
            }

            return carpeta;
        }

        private static string ObtenerCarpetaContrato(
    string directorioBase,
    string contratoFinal)
        {
            if (!Directory.Exists(directorioBase))
            {
                throw new Exception($"No existe la ruta {directorioBase}");
            }

            string numeroContrato = contratoFinal.Replace("Factura_", "");

            foreach (var carpetaUsuario in Directory.GetDirectories(directorioBase))
            {
                string carpetaContrato = Directory
                    .GetDirectories(carpetaUsuario)
                    .FirstOrDefault(d =>
                        Path.GetFileName(d)
                            .StartsWith(
                                numeroContrato,
                                StringComparison.OrdinalIgnoreCase));

                if (carpetaContrato != null)
                {
                    return carpetaContrato;
                }
            }

            return null;
        }

        private static string ObtenerRutaFacturas(string contrato, string usuario)
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

                ubicacion = ubicacion?.Trim().ToUpper();

                string rutaUsuario;
                string rutaDefault=string.Empty;

                switch (ubicacion)
                {
                    case "SPS":
                        //rutaUsuario = $@"\\10.10.1.31\ca-sps\{usuario}";
                        //rutaDefault = @"\\10.10.1.31\ca-sps\asistenteventassps";
                        rutaUsuario = $@"\\10.10.1.31\ca-sps";
                        break;

                    case "TGU":
                        //rutaUsuario = $@"\\10.10.1.31\ca-tgu\{usuario}";
                        //rutaDefault = @"\\10.10.1.31\ca-tgu\asistenteventastgu";
                        rutaUsuario = $@"\\10.10.1.31\ca-tgu";
                        break;

                    default:
                        throw new Exception(
                            $"No se encontró ubicación para el cost center '{costCenter}'");
                }

                string rutaFinal;

                try
                {
                    rutaFinal = Directory.Exists(rutaUsuario)
                        ? rutaUsuario
                        : rutaDefault;
                }
                catch
                {
                    rutaFinal = rutaDefault;
                }

                return rutaFinal;
            }
        }


    }
}
