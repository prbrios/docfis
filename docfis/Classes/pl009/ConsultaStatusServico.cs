using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.Serialization;

namespace docfis.Classes.pl009
{
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://www.portalfiscal.inf.br/nfe")]
    [System.Xml.Serialization.XmlRootAttribute("consStatServ", Namespace = "http://www.portalfiscal.inf.br/nfe", IsNullable = false)]
    public class ConsultaStatusServico : IConsultaStatusServico
    {
        public string tpAmb { get; set; }

        public string cUF { get; set; }

        public string xServ { get; set; }

        [System.Xml.Serialization.XmlAttributeAttribute(DataType = "token")]
        public string versao { get; set; }

        public ConsultaStatusServico()
        {

        }

        public ConsultaStatusServico(string tpAmb, string cUF, string xServ, string versao)
        {
            this.tpAmb = tpAmb;
            this.cUF = cUF;
            this.xServ = xServ;
            this.versao = versao;
        }

        public string Serialize()
        {
            using (StringWriter stringwriter = new StringWriter())
            {
                XmlWriter xw = XmlWriter.Create(stringwriter, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true });
                XmlSerializerNamespaces xsn = new XmlSerializerNamespaces();
                xsn.Add("", "http://www.portalfiscal.inf.br/nfe");
                var serializer = new XmlSerializer(this.GetType());
                serializer.Serialize(xw, this, xsn);
                return stringwriter.ToString();
            }
        }
    }
}