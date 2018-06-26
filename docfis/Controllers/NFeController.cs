using docfis.Services.NFe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using System.Web.Mvc;
using System.Xml;

namespace docfis.Controllers
{
    public class NFeController : Controller
    {
        [HttpPost]
        public string Enviar(DadosEnvio dados)
        {
            return dados.Token;
        }

        [HttpPost]
        [Route("statusservico")]
        public string StatusServico(DadosEnvio dados)
        {
            X509Certificate2 cert = Util.ObterCertificado("CN=BC MANUTENCAO DE VEICULOS EIRELI:24578949000131, OU=Autenticado por AR Servir, OU=RFB e-CNPJ A1, OU=Secretaria da Receita Federal do Brasil - RFB, L=Fortaleza, S=CE, O=ICP-Brasil, C=BR");

            string tpAmb = "2";
            string cUF = "23";

            string consStatServ = ""
            + "<consStatServ versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\">"
            + "<tpAmb>" + tpAmb + "</tpAmb>"
            + "<cUF>" + cUF + "</cUF>"
            + "<xServ>STATUS</xServ>"
            + "</consStatServ>";

            XmlDocument obj = new XmlDocument();
            obj.LoadXml(consStatServ);

            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3;
            NFeStatusServico4 servico = new NFeStatusServico4();
            servico.ClientCertificates.Add(cert);
            servico.Url = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeStatusServico4?WSDL";
            XmlNode node = servico.Execute(obj);

            return node.OuterXml;
        }

    }

    public class DadosEnvio
    {
        public string Token { get; set; }
    }

    public class Util
    {
        public static X509Certificate2 ObterCertificado(string nomeCertificado)
        {
            X509Store st = null;
            try
            {
                st = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                //st = new X509Store(StoreLocation.CurrentUser);
                st.Open(OpenFlags.ReadOnly);
                foreach (X509Certificate2 cert in st.Certificates)
                {
                    if (cert.Subject.Equals(nomeCertificado))
                        return cert;
                }
            }
            finally
            {
                if (st != null)
                    st.Close();
            }
            return null;
        }
    }

}