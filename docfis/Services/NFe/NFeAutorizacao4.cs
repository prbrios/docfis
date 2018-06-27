using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services.Protocols;

namespace docfis.Services.NFe
{
    [System.Web.Services.WebServiceBindingAttribute(Name = "NFeAutorizacaoSoap12", Namespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4")]
    public class NFeAutorizacao4 : SoapHttpClientProtocol
    {
        public NFeAutorizacao4()
        {
            this.SoapVersion = System.Web.Services.Protocols.SoapProtocolVersion.Soap12;
            this.Url = "http://nfe.sefaz.ce.gov.br:80/nfe4/services/NFeAutorizacao4";
        }

        /// <remarks/>
        [System.Web.Services.Protocols.SoapDocumentMethodAttribute("http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLote", Use = System.Web.Services.Description.SoapBindingUse.Literal, ParameterStyle = System.Web.Services.Protocols.SoapParameterStyle.Bare)]
        [return: System.Xml.Serialization.XmlElementAttribute("nfeResultMsg", Namespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4")]
        public System.Xml.XmlNode nfeAutorizacaoLote([System.Xml.Serialization.XmlElementAttribute(Namespace = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4")] System.Xml.XmlNode nfeDadosMsg)
        {
            object[] results = this.Invoke("nfeAutorizacaoLote", new object[] { nfeDadosMsg });
            return ((System.Xml.XmlNode)(results[0]));
        }
    }
}