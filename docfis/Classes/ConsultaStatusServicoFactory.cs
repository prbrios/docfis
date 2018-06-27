using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace docfis.Classes
{
    public class ConsultaStatusServicoFactory
    {

        private string _tpAmb;
        private string _cUF;

        public ConsultaStatusServicoFactory(string tbAmp, string cUF)
        {
            _tpAmb = tbAmp;
            _cUF = cUF;
        }

        public IConsultaStatusServico CriaConsultaStatusServico(string pl)
        {

            switch (pl)
            {
                case ">>> OutrasPLS...":
                    return null;

                case "pl_009":
                    return new pl009.ConsultaStatusServico(_tpAmb, _cUF, "STATUS", "4.00");

                default:
                    return new pl009.ConsultaStatusServico(_tpAmb, _cUF, "STATUS", "4.00");

            }

        }

    }
}