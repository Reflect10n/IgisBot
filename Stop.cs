using System;
using System.Collections.Generic;
using System.Text;

namespace IgisBot
{
    internal class Stop
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string ShortName { get; set; }
        public string Direction { get; set; }
        public string Type { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Obligation { get; set; }
    }
}
