using docfis.Classes;
using docfis.Models;
using docfis.Services.NFe;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using System.Xml.Serialization;

namespace docfis.Controllers
{
    [Route("[controller]")]
    public class NFeController : Controller
    {
        [HttpPost]
        [ValidateInput(false)]
        [Route("enviar")]
        public string Enviar(DadosEnvio dados)
        {

            Empresa empresa = new Empresa();

            X509Certificate2 cert = AssinaturaDigital.ObterCertificado(empresa.NomeCertificado);
            AssinaturaDigital assinatura = new AssinaturaDigital(cert);
            string xmlAssinado = assinatura.AssinarDocumento(dados.Xml);

            string enviNfe = "";
            enviNfe += "<enviNFe versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\">";
            enviNfe += "<idLote>" + dados.IdLote + "</idLote>";
            enviNfe += "<indSinc>0</indSinc>";
            enviNfe += xmlAssinado;
            enviNfe += "</enviNFe>";

            XmlDocument xDoc = new XmlDocument();
            xDoc.LoadXml(enviNfe);
            xDoc.CreateXmlDeclaration("1.0", "utf-8", "");

            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3;
            NFeAutorizacao4 servico = new NFeAutorizacao4();
            servico.ClientCertificates.Add(cert);
            servico.Url = Autorizador.ObterURLServico(empresa.ConfiguracaoNFe.Ambiente, TipoServico.AUTORIZACAO, empresa.Estado.UF, empresa.ConfiguracaoNFe.ModoEmissao);
            XmlNode node = servico.nfeAutorizacaoLote(xDoc);

            Response.ContentType = "application/xml";
            return node.OuterXml;

        }

        [HttpPost]
        [Route("statusservico")]
        public string StatusServico(DadosEnvio dados)
        {

            Response.ContentType = "text/plain";

            try
            {
                Empresa empresa = new Empresa();

                X509Certificate2 cert = AssinaturaDigital.ObterCertificado(empresa.NomeCertificado);

                var consultaStatusServico = new ConsultaStatusServicoFactory(empresa.ConfiguracaoNFe.Ambiente, empresa.Estado.Codigo)
                    .CriaConsultaStatusServico(empresa.ConfiguracaoNFe.PL);
                string consStatServ = consultaStatusServico.Serialize();

                XmlDocument obj = new XmlDocument();
                obj.LoadXml(consStatServ);

                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3;
                NFeStatusServico4 servico = new NFeStatusServico4();
                servico.ClientCertificates.Add(cert);
                servico.Url = Autorizador.ObterURLServico(empresa.ConfiguracaoNFe.Ambiente, TipoServico.STATUS_SERVICO, empresa.Estado.UF, empresa.ConfiguracaoNFe.ModoEmissao);
                XmlNode node = servico.Execute(obj);

                if(empresa.ConfiguracaoNFe.PL.Equals("pl_009"))
                {
                    using (StringReader stringReader = new System.IO.StringReader(node.OuterXml))
                    {
                        var serializer = new XmlSerializer(typeof(Classes.pl009.RetornoConsultaStatusServico));
                        Classes.pl009.RetornoConsultaStatusServico ret = (Classes.pl009.RetornoConsultaStatusServico) serializer.Deserialize(stringReader);
                        return $"{ret.cStat},{ret.xMotivo}";
                    }
                }

                return "";

            }catch(Exception ex)
            {
                return $"EXCEPTION,{ex.Message}";
            }
            
        }

    }

    public class DadosEnvio
    {
        public string Token { get; set; }
        public string Xml { get; set; }
        public string IdLote { get; set; }
    }

    public class AssinaturaDigital
    {

        private static string[] ELEMENTOS_ASSINAVEIS = {"infEvento", "infCanc", "infNFe", "infInut", "infMDFe", "infCTe" }; 

        X509Certificate2 _certificadoDigital;

        public AssinaturaDigital(X509Certificate2 cert)
        {
            _certificadoDigital = cert;
        }

        public string AssinarDocumento(string xml)
        {
            XmlDocument documento = new XmlDocument();
            documento.LoadXml(xml);

            foreach(string elemento in ELEMENTOS_ASSINAVEIS)
            {
                XmlNodeList ListInfNFe = documento.GetElementsByTagName(elemento);
                foreach (XmlElement infNFe in ListInfNFe)
                {
                    string id = infNFe.Attributes.GetNamedItem("Id").Value;
                    SignedXml signedXml = new SignedXml(infNFe);
                    signedXml.SigningKey = _certificadoDigital.PrivateKey;

                    Reference reference = new Reference("#" + id);
                    reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                    reference.AddTransform(new XmlDsigC14NTransform());
                    signedXml.AddReference(reference);

                    KeyInfo keyInfo = new KeyInfo();
                    keyInfo.AddClause(new KeyInfoX509Data(_certificadoDigital));
                    signedXml.KeyInfo = keyInfo;

                    signedXml.ComputeSignature();

                    XmlElement xmlSignature = documento.CreateElement("Signature", "http://www.w3.org/2000/09/xmldsig#");
                    XmlElement xmlSignedInfo = signedXml.SignedInfo.GetXml();
                    XmlElement xmlKeyInfo = signedXml.KeyInfo.GetXml();

                    XmlElement xmlSignatureValue = documento.CreateElement("SignatureValue", xmlSignature.NamespaceURI);
                    string signBase64 = Convert.ToBase64String(signedXml.Signature.SignatureValue);
                    XmlText text = documento.CreateTextNode(signBase64);
                    xmlSignatureValue.AppendChild(text);

                    XmlNode xmlNfe = infNFe.ParentNode;

                    xmlSignature.AppendChild(documento.ImportNode(xmlSignedInfo, true));
                    xmlSignature.AppendChild(xmlSignatureValue);
                    xmlSignature.AppendChild(documento.ImportNode(xmlKeyInfo, true));

                    xmlNfe.AppendChild(xmlSignature);

                }
            }

            return documento.InnerXml;
        }

        public static X509Certificate2 ObterCertificado(string nomeCertificado)
        {
            X509Store st = null;
            try
            {
                st = new X509Store(StoreName.My, StoreLocation.CurrentUser);
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

    public enum TipoServico
    {
        STATUS_SERVICO,
        AUTORIZACAO,
        RECEPCAO_EVENTO,
        RET_AUTORIZACAO,
        INUTILIZACAO,
        CONSULTA_PROTOCOLO,
        CONSULTA_CADASTRO
    }

    public class Autorizador
    {

        #region urls dos serviços AMAZONAS
        private static string AM_HOM_CONSULTA_PROTOCOLO = "https://homnfe.sefaz.am.gov.br/services2/services/NfeConsulta4";
        private static string AM_HOM_STATUS_SERVICO = "https://homnfe.sefaz.am.gov.br/services2/services/NfeStatusServico4";
        private static string AM_HOM_RECEPCAO_EVENTO = "https://homnfe.sefaz.am.gov.br/services2/services/RecepcaoEvento4";
        private static string AM_HOM_AUTORIZACAO = "https://homnfe.sefaz.am.gov.br/services2/services/NfeAutorizacao4";
        private static string AM_HOM_RET_AUTORIZACAO = "https://homnfe.sefaz.am.gov.br/services2/services/NfeRetAutorizacao4";
        private static string AM_HOM_INUTILIZACAO = "https://homnfe.sefaz.am.gov.br/services2/services/NfeInutilizacao4";
        private static string AM_HOM_CONSULTA_CADASTRO = "";

        private static string AM_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefaz.am.gov.br/services2/services/NfeConsulta4";
        private static string AM_PRO_STATUS_SERVICO = "https://nfe.sefaz.am.gov.br/services2/services/NfeStatusServico4";
        private static string AM_PRO_RECEPCAO_EVENTO = "https://nfe.sefaz.am.gov.br/services2/services/RecepcaoEvento4";
        private static string AM_PRO_AUTORIZACAO = "https://nfe.sefaz.am.gov.br/services2/services/NfeAutorizacao4";
        private static string AM_PRO_RET_AUTORIZACAO = "https://nfe.sefaz.am.gov.br/services2/services/NfeRetAutorizacao4";
        private static string AM_PRO_INUTILIZACAO = "https://nfe.sefaz.am.gov.br/services2/services/NfeInutilizacao4";
        private static string AM_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços BAHIA
        private static string BA_HOM_CONSULTA_PROTOCOLO = "https://hnfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        private static string BA_HOM_STATUS_SERVICO = "https://hnfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx";
        private static string BA_HOM_RECEPCAO_EVENTO = "https://hnfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        private static string BA_HOM_AUTORIZACAO = "https://hnfe.sefaz.ba.gov.br/webservices/NFeAutorizacao4/NFeAutorizacao4.asmx";
        private static string BA_HOM_RET_AUTORIZACAO = "https://hnfe.sefaz.ba.gov.br/webservices/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        private static string BA_HOM_INUTILIZACAO = "https://hnfe.sefaz.ba.gov.br/webservices/NFeInutilizacao4/NFeInutilizacao4.asmx";
        private static string BA_HOM_CONSULTA_CADASTRO = "";

        private static string BA_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        private static string BA_PRO_STATUS_SERVICO = "https://nfe.sefaz.ba.gov.br/webservices/NFeStatusServico4/NFeStatusServico4.asmx";
        private static string BA_PRO_RECEPCAO_EVENTO = "https://nfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        private static string BA_PRO_AUTORIZACAO = "https://nfe.sefaz.ba.gov.br/webservices/NFeAutorizacao4/NFeAutorizacao4.asmx";
        private static string BA_PRO_RET_AUTORIZACAO = "https://nfe.sefaz.ba.gov.br/webservices/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        private static string BA_PRO_INUTILIZACAO = "https://nfe.sefaz.ba.gov.br/webservices/NFeInutilizacao4/NFeInutilizacao4.asmx";
        private static string BA_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços CEARA
        private static string CE_HOM_CONSULTA_PROTOCOLO = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeConsultaProtocolo4?WSDL";
        private static string CE_HOM_STATUS_SERVICO = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeStatusServico4?WSDL";
        private static string CE_HOM_RECEPCAO_EVENTO = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeRecepcaoEvento4?WSDL";
        private static string CE_HOM_AUTORIZACAO = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeAutorizacao4?WSDL";
        private static string CE_HOM_RET_AUTORIZACAO = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeRetAutorizacao4?WSDL";
        private static string CE_HOM_INUTILIZACAO = "https://nfeh.sefaz.ce.gov.br/nfe4/services/NFeInutilizacao4?WSDL";
        private static string CE_HOM_CONSULTA_CADASTRO = "";

        private static string CE_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefaz.ce.gov.br/nfe4/services/NFeConsultaProtocolo4?wsdl";
        private static string CE_PRO_STATUS_SERVICO = "https://nfe.sefaz.ce.gov.br/nfe4/services/NFeStatusServico4?wsdl";
        private static string CE_PRO_RECEPCAO_EVENTO = "https://nfe.sefaz.ce.gov.br/nfe4/services/NFeRecepcaoEvento4?wsdl";
        private static string CE_PRO_AUTORIZACAO = "https://nfe.sefaz.ce.gov.br/nfe4/services/NFeAutorizacao4?wsdl";
        private static string CE_PRO_RET_AUTORIZACAO = "https://nfe.sefaz.ce.gov.br/nfe4/services/NFeRetAutorizacao4?wsdl";
        private static string CE_PRO_INUTILIZACAO = "https://nfe.sefaz.ce.gov.br/nfe4/services/NFeInutilizacao4?wsdl";
        private static string CE_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços GOIAS
        private static string GO_HOM_CONSULTA_PROTOCOLO = "https://homolog.sefaz.go.gov.br/nfe/services/NFeConsultaProtocolo4?wsdl";
        private static string GO_HOM_STATUS_SERVICO = "https://homolog.sefaz.go.gov.br/nfe/services/NFeStatusServico4?wsdl";
        private static string GO_HOM_RECEPCAO_EVENTO = "https://homolog.sefaz.go.gov.br/nfe/services/NFeRecepcaoEvento4?wsdl";
        private static string GO_HOM_AUTORIZACAO = "https://homolog.sefaz.go.gov.br/nfe/services/NFeAutorizacao4?wsdl";
        private static string GO_HOM_RET_AUTORIZACAO = "https://homolog.sefaz.go.gov.br/nfe/services/NFeRetAutorizacao4?wsdl";
        private static string GO_HOM_INUTILIZACAO = "https://homolog.sefaz.go.gov.br/nfe/services/NFeInutilizacao4?wsdl";
        private static string GO_HOM_CONSULTA_CADASTRO = "";

        private static string GO_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefaz.go.gov.br/nfe/services/NFeConsultaProtocolo4?wsdl";
        private static string GO_PRO_STATUS_SERVICO = "https://nfe.sefaz.go.gov.br/nfe/services/NFeStatusServico4?wsdl";
        private static string GO_PRO_RECEPCAO_EVENTO = "https://nfe.sefaz.go.gov.br/nfe/services/NFeRecepcaoEvento4?wsdl";
        private static string GO_PRO_AUTORIZACAO = "https://nfe.sefaz.go.gov.br/nfe/services/NFeAutorizacao4?wsdl";
        private static string GO_PRO_RET_AUTORIZACAO = "https://nfe.sefaz.go.gov.br/nfe/services/NFeRetAutorizacao4?wsdl";
        private static string GO_PRO_INUTILIZACAO = "https://nfe.sefaz.go.gov.br/nfe/services/NFeInutilizacao4?wsdl";
        private static string GO_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços MINAS GERAIS
        private static string MG_HOM_CONSULTA_PROTOCOLO = "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4";
        private static string MG_HOM_STATUS_SERVICO = "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeStatusServico4";
        private static string MG_HOM_RECEPCAO_EVENTO = "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4";
        private static string MG_HOM_AUTORIZACAO = "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4";
        private static string MG_HOM_RET_AUTORIZACAO = "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeRetAutorizacao4";
        private static string MG_HOM_INUTILIZACAO = "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeInutilizacao4";
        private static string MG_HOM_CONSULTA_CADASTRO = "";

        private static string MG_PRO_CONSULTA_PROTOCOLO = "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4";
        private static string MG_PRO_STATUS_SERVICO = "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeStatusServico4";
        private static string MG_PRO_RECEPCAO_EVENTO = "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4";
        private static string MG_PRO_AUTORIZACAO = "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4";
        private static string MG_PRO_RET_AUTORIZACAO = "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeRetAutorizacao4";
        private static string MG_PRO_INUTILIZACAO = "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeInutilizacao4";
        private static string MG_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços MATO GROSSO DO SUL
        private static string MS_HOM_CONSULTA_PROTOCOLO = "https://hom.nfe.sefaz.ms.gov.br/ws/NFeConsultaProtocolo4";
        private static string MS_HOM_STATUS_SERVICO = "https://hom.nfe.sefaz.ms.gov.br/ws/NFeStatusServico4";
        private static string MS_HOM_RECEPCAO_EVENTO = "https://hom.nfe.sefaz.ms.gov.br/ws/NFeRecepcaoEvento4";
        private static string MS_HOM_AUTORIZACAO = "https://hom.nfe.sefaz.ms.gov.br/ws/NFeAutorizacao4";
        private static string MS_HOM_RET_AUTORIZACAO = "https://hom.nfe.sefaz.ms.gov.br/ws/NFeRetAutorizacao4";
        private static string MS_HOM_INUTILIZACAO = "https://hom.nfe.sefaz.ms.gov.br/ws/NFeInutilizacao4";
        private static string MS_HOM_CONSULTA_CADASTRO = "";

        private static string MS_PRO_CONSULTA_PROTOCOLO = "https://nfe.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4";
        private static string MS_PRO_STATUS_SERVICO = "https://nfe.fazenda.ms.gov.br/ws/NFeStatusServico4";
        private static string MS_PRO_RECEPCAO_EVENTO = "https://nfe.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4";
        private static string MS_PRO_AUTORIZACAO = "https://nfe.fazenda.ms.gov.br/ws/NFeAutorizacao4";
        private static string MS_PRO_RET_AUTORIZACAO = "https://nfe.fazenda.ms.gov.br/ws/NFeRetAutorizacao4";
        private static string MS_PRO_INUTILIZACAO = "https://nfe.fazenda.ms.gov.br/ws/NFeInutilizacao4";
        private static string MS_PRO_CONSULTA_CADASTRO = "";
        #endregion


        #region urls dos serviços MATO GROSSO
        private static string MT_HOM_CONSULTA_PROTOCOLO = "https://homologacao.sefaz.mt.gov.br/nfews/v2/services/NfeConsulta4?wsdl";
        private static string MT_HOM_STATUS_SERVICO = "https://homologacao.sefaz.mt.gov.br/nfews/v2/services/NfeStatusServico4?wsdl";
        private static string MT_HOM_RECEPCAO_EVENTO = "https://homologacao.sefaz.mt.gov.br/nfews/v2/services/RecepcaoEvento4?wsdl";
        private static string MT_HOM_AUTORIZACAO = "https://homologacao.sefaz.mt.gov.br/nfews/v2/services/NfeAutorizacao4?wsdl";
        private static string MT_HOM_RET_AUTORIZACAO = "https://homologacao.sefaz.mt.gov.br/nfews/v2/services/NfeRetAutorizacao4?wsdl";
        private static string MT_HOM_INUTILIZACAO = "https://homologacao.sefaz.mt.gov.br/nfews/v2/services/NfeInutilizacao4?wsdl";
        private static string MT_HOM_CONSULTA_CADASTRO = "";

        private static string MT_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefaz.mt.gov.br/nfews/v2/services/NfeConsulta4?wsdl";
        private static string MT_PRO_STATUS_SERVICO = "https://nfe.sefaz.mt.gov.br/nfews/v2/services/NfeStatusServico4?wsdl";
        private static string MT_PRO_RECEPCAO_EVENTO = "https://nfe.sefaz.mt.gov.br/nfews/v2/services/RecepcaoEvento4?wsdl";
        private static string MT_PRO_AUTORIZACAO = "https://nfe.sefaz.mt.gov.br/nfews/v2/services/NfeAutorizacao4?wsdl";
        private static string MT_PRO_RET_AUTORIZACAO = "https://nfe.sefaz.mt.gov.br/nfews/v2/services/NfeRetAutorizacao4?wsdl";
        private static string MT_PRO_INUTILIZACAO = "https://nfe.sefaz.mt.gov.br/nfews/v2/services/NfeInutilizacao4?wsdl";
        private static string MT_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços PERNAMBUCO
        private static string PE_HOM_CONSULTA_PROTOCOLO = "https://nfehomolog.sefaz.pe.gov.br/nfe-service/services/NFeConsultaProtocolo4";
        private static string PE_HOM_STATUS_SERVICO = "https://nfehomolog.sefaz.pe.gov.br/nfe-service/services/NFeStatusServico4";
        private static string PE_HOM_RECEPCAO_EVENTO = "https://nfehomolog.sefaz.pe.gov.br/nfe-service/services/NFeRecepcaoEvento4";
        private static string PE_HOM_AUTORIZACAO = "https://nfehomolog.sefaz.pe.gov.br/nfe-service/services/NFeAutorizacao4";
        private static string PE_HOM_RET_AUTORIZACAO = "https://nfehomolog.sefaz.pe.gov.br/nfe-service/services/NFeRetAutorizacao4";
        private static string PE_HOM_INUTILIZACAO = "https://nfehomolog.sefaz.pe.gov.br/nfe-service/services/NFeInutilizacao4";
        private static string PE_HOM_CONSULTA_CADASTRO = "";

        private static string PE_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeConsultaProtocolo4";
        private static string PE_PRO_STATUS_SERVICO = "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeStatusServico4";
        private static string PE_PRO_RECEPCAO_EVENTO = "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeRecepcaoEvento4";
        private static string PE_PRO_AUTORIZACAO = "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeAutorizacao4";
        private static string PE_PRO_RET_AUTORIZACAO = "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeRetAutorizacao4";
        private static string PE_PRO_INUTILIZACAO = "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeInutilizacao4";
        private static string PE_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços PARANA
        private static string PR_HOM_CONSULTA_PROTOCOLO = "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeConsultaProtocolo4?wsdl";
        private static string PR_HOM_STATUS_SERVICO = "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeStatusServico4?wsdl";
        private static string PR_HOM_RECEPCAO_EVENTO = "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeRecepcaoEvento4?wsdl";
        private static string PR_HOM_AUTORIZACAO = "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeAutorizacao4?wsdl";
        private static string PR_HOM_RET_AUTORIZACAO = "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeRetAutorizacao4?wsdl";
        private static string PR_HOM_INUTILIZACAO = "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeInutilizacao4?wsdl";
        private static string PR_HOM_CONSULTA_CADASTRO = "";

        private static string PR_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefa.pr.gov.br/nfe/NFeConsultaProtocolo4?wsdl";
        private static string PR_PRO_STATUS_SERVICO = "https://nfe.sefa.pr.gov.br/nfe/NFeStatusServico4?wsdl";
        private static string PR_PRO_RECEPCAO_EVENTO = "https://nfe.sefa.pr.gov.br/nfe/NFeRecepcaoEvento4?wsdl";
        private static string PR_PRO_AUTORIZACAO = "https://nfe.sefa.pr.gov.br/nfe/NFeAutorizacao4?wsdl";
        private static string PR_PRO_RET_AUTORIZACAO = "https://nfe.sefa.pr.gov.br/nfe/NFeRetAutorizacao4?wsdl";
        private static string PR_PRO_INUTILIZACAO = "https://nfe.sefa.pr.gov.br/nfe/NFeInutilizacao4?wsdl";
        private static string PR_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços RIO GRANDE DO SUL
        private static string RS_HOM_CONSULTA_PROTOCOLO = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
        private static string RS_HOM_STATUS_SERVICO = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
        private static string RS_HOM_RECEPCAO_EVENTO = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
        private static string RS_HOM_AUTORIZACAO = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
        private static string RS_HOM_RET_AUTORIZACAO = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx";
        private static string RS_HOM_INUTILIZACAO = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/nfeinutilizacao/nfeinutilizacao4.asmx";
        private static string RS_HOM_CONSULTA_CADASTRO = "";

        private static string RS_PRO_CONSULTA_PROTOCOLO = "https://nfe.sefazrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
        private static string RS_PRO_STATUS_SERVICO = "https://nfe.sefazrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
        private static string RS_PRO_RECEPCAO_EVENTO = "https://nfe.sefazrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
        private static string RS_PRO_AUTORIZACAO = "https://nfe.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
        private static string RS_PRO_RET_AUTORIZACAO = "https://nfe.sefazrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx";
        private static string RS_PRO_INUTILIZACAO = "https://nfe.sefazrs.rs.gov.br/ws/nfeinutilizacao/nfeinutilizacao4.asmx";
        private static string RS_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços SAO PAULO
        private static string SP_HOM_CONSULTA_PROTOCOLO = "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfeconsultaprotocolo4.asmx";
        private static string SP_HOM_STATUS_SERVICO = "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfestatusservico4.asmx";
        private static string SP_HOM_RECEPCAO_EVENTO = "https://homologacao.nfe.fazenda.sp.gov.br/ws/nferecepcaoevento4.asmx";
        private static string SP_HOM_AUTORIZACAO = "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfeautorizacao4.asmx";
        private static string SP_HOM_RET_AUTORIZACAO = "https://homologacao.nfe.fazenda.sp.gov.br/ws/nferetautorizacao4.asmx";
        private static string SP_HOM_INUTILIZACAO = "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfeinutilizacao4.asmx";
        private static string SP_HOM_CONSULTA_CADASTRO = "";

        private static string SP_PRO_CONSULTA_PROTOCOLO = "https://nfe.fazenda.sp.gov.br/ws/nfeconsultaprotocolo4.asmx";
        private static string SP_PRO_STATUS_SERVICO = "https://nfe.fazenda.sp.gov.br/ws/nfestatusservico4.asmx";
        private static string SP_PRO_RECEPCAO_EVENTO = "https://nfe.fazenda.sp.gov.br/ws/nferecepcaoevento4.asmx";
        private static string SP_PRO_AUTORIZACAO = "https://nfe.fazenda.sp.gov.br/ws/nfeautorizacao4.asmx";
        private static string SP_PRO_RET_AUTORIZACAO = "https://nfe.fazenda.sp.gov.br/ws/nferetautorizacao4.asmx";
        private static string SP_PRO_INUTILIZACAO = "https://nfe.fazenda.sp.gov.br/ws/nfeinutilizacao4.asmx";
        private static string SP_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços SVAN
        private static string SVAN_HOM_CONSULTA_PROTOCOLO = "https://hom.sefazvirtual.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        private static string SVAN_HOM_STATUS_SERVICO = "https://hom.sefazvirtual.fazenda.gov.br/NFeStatusServico4/NFeStatusServico4.asmx";
        private static string SVAN_HOM_RECEPCAO_EVENTO = "https://hom.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        private static string SVAN_HOM_AUTORIZACAO = "https://hom.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";
        private static string SVAN_HOM_RET_AUTORIZACAO = "https://hom.sefazvirtual.fazenda.gov.br/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        private static string SVAN_HOM_INUTILIZACAO = "https://hom.sefazvirtual.fazenda.gov.br/NFeInutilizacao4/NFeInutilizacao4.asmx";
        private static string SVAN_HOM_CONSULTA_CADASTRO = "";

        private static string SVAN_PRO_CONSULTA_PROTOCOLO = "https://www.sefazvirtual.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        private static string SVAN_PRO_STATUS_SERVICO = "https://www.sefazvirtual.fazenda.gov.br/NFeStatusServico4/NFeStatusServico4.asmx";
        private static string SVAN_PRO_RECEPCAO_EVENTO = "https://www.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        private static string SVAN_PRO_AUTORIZACAO = "https://www.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";
        private static string SVAN_PRO_RET_AUTORIZACAO = "https://www.sefazvirtual.fazenda.gov.br/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        private static string SVAN_PRO_INUTILIZACAO = "https://www.sefazvirtual.fazenda.gov.br/NFeInutilizacao4/NFeInutilizacao4.asmx";
        private static string SVAN_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region urls dos serviços SVRS
        private static string SVRS_HOM_CONSULTA_PROTOCOLO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
        private static string SVRS_HOM_STATUS_SERVICO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
        private static string SVRS_HOM_RECEPCAO_EVENTO = "https://nfe-homologacao.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
        private static string SVRS_HOM_AUTORIZACAO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
        private static string SVRS_HOM_RET_AUTORIZACAO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx";
        private static string SVRS_HOM_INUTILIZACAO = "https://nfe-homologacao.svrs.rs.gov.br/ws/nfeinutilizacao/nfeinutilizacao4.asmx";
        private static string SVRS_HOM_CONSULTA_CADASTRO = "";

        private static string SVRS_PRO_CONSULTA_PROTOCOLO = "https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
        private static string SVRS_PRO_STATUS_SERVICO = "https://nfe.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
        private static string SVRS_PRO_RECEPCAO_EVENTO = "https://nfe.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
        private static string SVRS_PRO_AUTORIZACAO = "https://nfe.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
        private static string SVRS_PRO_RET_AUTORIZACAO = "https://nfe.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx";
        private static string SVRS_PRO_INUTILIZACAO = "https://nfe.svrs.rs.gov.br/ws/nfeinutilizacao/nfeinutilizacao4.asmx";
        private static string SVRS_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region url dos servico SVC-AN
        private static string SVC_AN_HOM_CONSULTA_PROTOCOLO = "https://hom.svc.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        private static string SVC_AN_HOM_STATUS_SERVICO = "https://hom.svc.fazenda.gov.br/NFeStatusServico4/NFeStatusServico4.asmx";
        private static string SVC_AN_HOM_RECEPCAO_EVENTO = "https://hom.svc.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        private static string SVC_AN_HOM_AUTORIZACAO = "https://hom.svc.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";
        private static string SVC_AN_HOM_RET_AUTORIZACAO = "https://hom.svc.fazenda.gov.br/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        private static string SVC_AN_HOM_CONSULTA_CADASTRO = "";

        private static string SVC_AN_PRO_CONSULTA_PROTOCOLO = "https://www.svc.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        private static string SVC_AN_PRO_STATUS_SERVICO = "https://www.svc.fazenda.gov.br/NFeStatusServico4/NFeStatusServico4.asmx";
        private static string SVC_AN_PRO_RECEPCAO_EVENTO = "https://www.svc.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        private static string SVC_AN_PRO_AUTORIZACAO = "https://www.svc.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";
        private static string SVC_AN_PRO_RET_AUTORIZACAO = "https://www.svc.fazenda.gov.br/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        private static string SVC_AN_PRO_CONSULTA_CADASTRO = "";
        #endregion

        #region url dos serviços SVC-RS
        private static string SVC_RS_HOM_CONSULTA_PROTOCOLO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
        private static string SVC_RS_HOM_STATUS_SERVICO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
        private static string SVC_RS_HOM_RECEPCAO_EVENTO = "https://nfe-homologacao.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
        private static string SVC_RS_HOM_AUTORIZACAO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
        private static string SVC_RS_HOM_RET_AUTORIZACAO = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx";
        private static string SVC_RS_HOM_CONSULTA_CADASTRO = "";

        private static string SVC_RS_PRO_CONSULTA_PROTOCOLO = "https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
        private static string SVC_RS_PRO_STATUS_SERVICO = "https://nfe.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
        private static string SVC_RS_PRO_RECEPCAO_EVENTO = "https://nfe.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
        private static string SVC_RS_PRO_AUTORIZACAO = "https://nfe.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
        private static string SVC_RS_PRO_RET_AUTORIZACAO = "https://nfe.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx";
        private static string SVC_RS_PRO_CONSULTA_CADASTRO = "";
        #endregion

        public static string ObterURLServico(string ambiente, TipoServico tipoServico, string UF, string modo)
        {
            if (modo.Equals("SVC"))
            {
                if (new List<string>() { "AM", "BA", "CE", "GO", "MA", "MS", "MT", "PA", "PE", "PR" }.Contains(UF))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? SVC_RS_HOM_STATUS_SERVICO : SVC_RS_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? SVC_RS_HOM_RET_AUTORIZACAO : SVC_RS_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? SVC_RS_HOM_RECEPCAO_EVENTO : SVC_RS_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? SVC_RS_HOM_AUTORIZACAO : SVC_RS_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? SVC_RS_HOM_CONSULTA_PROTOCOLO : SVC_RS_PRO_CONSULTA_PROTOCOLO;
                }
                else
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? SVC_AN_HOM_STATUS_SERVICO : SVC_AN_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? SVC_AN_HOM_RET_AUTORIZACAO : SVC_AN_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? SVC_AN_HOM_RECEPCAO_EVENTO : SVC_AN_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? SVC_AN_HOM_AUTORIZACAO : SVC_AN_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? SVC_AN_HOM_CONSULTA_PROTOCOLO : SVC_AN_PRO_CONSULTA_PROTOCOLO;
                }
            }
            else if (modo.Equals("NORMAL"))
            {

                if (UF.Equals("AM"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? AM_HOM_STATUS_SERVICO : AM_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? AM_HOM_RET_AUTORIZACAO : AM_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? AM_HOM_RECEPCAO_EVENTO : AM_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? AM_HOM_AUTORIZACAO : AM_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? AM_HOM_INUTILIZACAO : AM_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? AM_HOM_CONSULTA_PROTOCOLO : AM_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("BA"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? BA_HOM_STATUS_SERVICO : BA_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? BA_HOM_RET_AUTORIZACAO : BA_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? BA_HOM_RECEPCAO_EVENTO : BA_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? BA_HOM_AUTORIZACAO : BA_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? BA_HOM_INUTILIZACAO : BA_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? BA_HOM_CONSULTA_PROTOCOLO : BA_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("CE"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? CE_HOM_STATUS_SERVICO : CE_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? CE_HOM_RET_AUTORIZACAO : CE_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? CE_HOM_RECEPCAO_EVENTO : CE_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? CE_HOM_AUTORIZACAO : CE_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? CE_HOM_INUTILIZACAO : CE_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? CE_HOM_CONSULTA_PROTOCOLO : CE_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("GO"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? GO_HOM_STATUS_SERVICO : GO_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? GO_HOM_RET_AUTORIZACAO : GO_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? GO_HOM_RECEPCAO_EVENTO : GO_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? GO_HOM_AUTORIZACAO : GO_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? GO_HOM_INUTILIZACAO : GO_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? GO_HOM_CONSULTA_PROTOCOLO : GO_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("MG"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? MG_HOM_STATUS_SERVICO : MG_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? MG_HOM_RET_AUTORIZACAO : MG_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? MG_HOM_RECEPCAO_EVENTO : MG_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? MG_HOM_AUTORIZACAO : MG_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? MG_HOM_INUTILIZACAO : MG_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? MG_HOM_CONSULTA_PROTOCOLO : MG_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("MS"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? MS_HOM_STATUS_SERVICO : MS_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? MS_HOM_RET_AUTORIZACAO : MS_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? MS_HOM_RECEPCAO_EVENTO : MS_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? MS_HOM_AUTORIZACAO : MS_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? MS_HOM_INUTILIZACAO : MS_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? MS_HOM_CONSULTA_PROTOCOLO : MS_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("MT"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? MT_HOM_STATUS_SERVICO : MT_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? MT_HOM_RET_AUTORIZACAO : MT_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? MT_HOM_RECEPCAO_EVENTO : MT_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? MT_HOM_AUTORIZACAO : MT_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? MT_HOM_INUTILIZACAO : MT_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? MT_HOM_CONSULTA_PROTOCOLO : MT_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("PE"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? PE_HOM_STATUS_SERVICO : PE_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? PE_HOM_RET_AUTORIZACAO : PE_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? PE_HOM_RECEPCAO_EVENTO : PE_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? PE_HOM_AUTORIZACAO : PE_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? PE_HOM_INUTILIZACAO : PE_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? PE_HOM_CONSULTA_PROTOCOLO : PE_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("PR"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? PR_HOM_STATUS_SERVICO : PR_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? PR_HOM_RET_AUTORIZACAO : PR_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? PR_HOM_RECEPCAO_EVENTO : PR_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? PR_HOM_AUTORIZACAO : PR_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? PR_HOM_INUTILIZACAO : PR_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? PR_HOM_CONSULTA_PROTOCOLO : PR_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("RS"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? RS_HOM_STATUS_SERVICO : RS_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? RS_HOM_RET_AUTORIZACAO : RS_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? RS_HOM_RECEPCAO_EVENTO : RS_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? RS_HOM_AUTORIZACAO : RS_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? RS_HOM_INUTILIZACAO : RS_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? RS_HOM_CONSULTA_PROTOCOLO : RS_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("SP"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? SP_HOM_STATUS_SERVICO : SP_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? SP_HOM_RET_AUTORIZACAO : SP_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? SP_HOM_RECEPCAO_EVENTO : SP_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? SP_HOM_AUTORIZACAO : SP_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? SP_HOM_INUTILIZACAO : SP_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? SP_HOM_CONSULTA_PROTOCOLO : SP_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (UF.Equals("MA") || UF.Equals("PA"))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? SVAN_HOM_STATUS_SERVICO : SVAN_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? SVAN_HOM_RET_AUTORIZACAO : SVAN_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? SVAN_HOM_RECEPCAO_EVENTO : SVAN_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? SVAN_HOM_AUTORIZACAO : SVAN_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? SVAN_HOM_INUTILIZACAO : SVAN_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? SVAN_HOM_CONSULTA_PROTOCOLO : SVAN_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
                else if (new List<string>() { "AC", "AL", "AP", "DF", "ES", "PB","PI", "RJ", "RN", "RO", "RR", "SC", "SE", "TO"}.Contains(UF))
                {
                    if (tipoServico == TipoServico.STATUS_SERVICO) return ambiente.Equals("2") ? SVRS_HOM_STATUS_SERVICO : SVRS_PRO_STATUS_SERVICO;
                    if (tipoServico == TipoServico.RET_AUTORIZACAO) return ambiente.Equals("2") ? SVRS_HOM_RET_AUTORIZACAO : SVRS_PRO_RET_AUTORIZACAO;
                    if (tipoServico == TipoServico.RECEPCAO_EVENTO) return ambiente.Equals("2") ? SVRS_HOM_RECEPCAO_EVENTO : SVRS_PRO_RECEPCAO_EVENTO;
                    if (tipoServico == TipoServico.AUTORIZACAO) return ambiente.Equals("2") ? SVRS_HOM_AUTORIZACAO : SVRS_PRO_AUTORIZACAO;
                    if (tipoServico == TipoServico.INUTILIZACAO) return ambiente.Equals("2") ? SVRS_HOM_INUTILIZACAO : SVRS_PRO_INUTILIZACAO;
                    if (tipoServico == TipoServico.CONSULTA_PROTOCOLO) return ambiente.Equals("2") ? SVRS_HOM_CONSULTA_PROTOCOLO : SVRS_PRO_CONSULTA_PROTOCOLO;
                    if (tipoServico == TipoServico.CONSULTA_CADASTRO) return ambiente.Equals("2") ? "" : "";
                }
            }

            return null;
        }
    }

}

namespace docfis.Models
{
    public class ConfiguracaoNFe
    {
        public string Ambiente { get; set; }
        public string ModoEmissao { get; set; }
        public string PL { get; set; }
    }

    public class Estado
    {
        public string UF { get; set; }
        public string Nome { get; set; }
        public string Codigo { get; set; }
    }

    public class Empresa
    {
        public string NomeCertificado { get; set; }
        public ConfiguracaoNFe ConfiguracaoNFe { get; set; }
        public Estado Estado;


        public Empresa()
        {
            Estado estado = new Estado();
            estado.Codigo = "23";
            estado.Nome = "Ceará";
            estado.UF = "CE";

            ConfiguracaoNFe configuracaoNFe = new ConfiguracaoNFe();
            configuracaoNFe.Ambiente = "2";
            configuracaoNFe.ModoEmissao = "NORMAL";
            configuracaoNFe.PL = "pl_009";

            NomeCertificado = "CN=SAMPAIO FILHO COMERCIO DE TECIDOS LTDA:05356167000103, OU=Autenticado por AR FACC, OU=RFB e-CNPJ A1, OU=Secretaria da Receita Federal do Brasil - RFB, L=Fortaleza, S=CE, O=ICP-Brasil, C=BR";
            ConfiguracaoNFe = configuracaoNFe;
            Estado = estado;

        }
    }
}