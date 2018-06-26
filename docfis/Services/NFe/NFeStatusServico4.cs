using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services;
using System.Diagnostics;
using System.Web.Services.Protocols;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Web.Services.Description;

namespace docfis.Services.NFe
{
    [WebServiceBinding(Name = "NfeStatusServico4Soap12", Namespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4")]
    public class NFeStatusServico4 : SoapHttpClientProtocol
    {
        public NFeStatusServico4()
        {

        }

        public NFeStatusServico4(string url, X509Certificate certificado, int timeOut)
        {
            SoapVersion = SoapProtocolVersion.Soap12;
            Url = url;
            Timeout = timeOut;
            ClientCertificates.Add(certificado);
        }

        [SoapDocumentMethod("http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4/nfeStatusServicoNF", Use = SoapBindingUse.Literal, ParameterStyle = SoapParameterStyle.Bare)]
        [WebMethod(MessageName = "nfeStatusServicoNF")]
        [return: XmlElement(Namespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4", ElementName = "nfeResultMsg")]
        public XmlNode Execute([XmlElement(Namespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4", ElementName = "nfeDadosMsg")] XmlNode nfeDadosMsg)
        {
            var results = Invoke("nfeStatusServicoNF", new object[] { nfeDadosMsg });
            return (XmlNode)(results[0]);
        }
    }
}