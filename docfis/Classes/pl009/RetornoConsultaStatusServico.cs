using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Xml.Serialization;

namespace docfis.Classes.pl009
{
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://www.portalfiscal.inf.br/nfe")]
    [System.Xml.Serialization.XmlRootAttribute("retConsStatServ", Namespace = "http://www.portalfiscal.inf.br/nfe", IsNullable = false)]
    public class RetornoConsultaStatusServico
    {

        public string tpAmb;
        public string verAplic;
        public string cStat;
        public string xMotivo;
        public string cUF;
        public string dhRecbto;
        public string tMed;
        public string dhRetorno;
        public string xObs;
        [System.Xml.Serialization.XmlAttributeAttribute(DataType = "token")]
        public string versao;

    }
}