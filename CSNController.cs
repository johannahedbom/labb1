using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using CSN_Lab_Shell.Models;
using Microsoft.Data.Sqlite;
using System.Xml.Linq;

namespace CSN_Lab_Shell.Controllers
{
    public class CSNController : Controller
    {
        SqliteConnection sqlite;

        public CSNController()
        {
            sqlite = new SqliteConnection("Data Source=csn.db");
        }

        async Task<XElement> SQLResult(string query, string root, string nodeName)
        {
            var xml = new XElement(root);

            try
            {
                await sqlite.OpenAsync();

                using (var command = new SqliteCommand(query, sqlite))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var element = new XElement(nodeName);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var value = await reader.GetFieldValueAsync<object>(i) ?? "";
                            element.Add(new XElement(reader.GetName(i), value));
                        }
                        xml.Add(element);
                    }
                }
            }
            finally
            {
                await sqlite.CloseAsync();
            }

            return xml;
        }

        //
        // GET: /Csn/Test
        // 
        // Testmetod som visar på hur ni kan arbeta från SQL -> XML ->
        // presentationsxml -> vyn/gränssnittet.
        // 
        // 1. En SQL-förfrågan genereras i strängformat (notera @ för att inkludera flera rader)
        // 2. Denna SQL-förfrågan skickas, tillsammans med ett rotnodsnamn och elementnamn, till SQLResult - som i sin tur skickar tillbaka en XML
        // 3. Med detta XElement-objekt kan vi sedan lägga till nya noder med Add, utföra kompletterande beräkningar och dylikt.
        // 4. XML:en skickas sedan till vårt gränssnitt (motsvarande .cshtml-fil i mappen Views -> CSN).
        public ActionResult Test()
        {
            string query = @"SELECT a.Arendenummer, s.Beskrivning, SUM(((Sluttid-starttid +1) * b.Belopp)) as Summa
                            FROM Arende a, Belopp b, BeviljadTid bt, BeviljadTid_Belopp btb, Stodform s, Beloppstyp blt
                            WHERE a.Arendenummer = bt.Arendenummer AND s.Stodformskod = a.Stodformskod
                            AND btb.BeloppID = b.BeloppID AND btb.BeviljadTidID = bt.BeviljadTidID AND b.Beloppstypkod = blt.Beloppstypkod AND b.BeloppID LIKE '%2009'
							Group by a.Arendenummer
							Order by a.Arendenummer ASC";
            XElement test = SQLResult(query, "BeviljadeTider2009", "BeviljadTid").Result;
            XElement summa = new XElement("Total",
                (from b in test.Descendants("Summa")
                 select (int)b).Sum());
            test.Add(summa);

            // skicka presentationsxml:n till vyn /Views/Csn/Test,
            // i vyn kommer vi sedan åt den genom variabeln "Model"
            return View(test);
        }

        //
        // GET: /Csn/Index
        public ActionResult Index()
        {
            return View();
        }


        //
        // GET: /Csn/Uppgift1
public ActionResult Uppgift1()
{
    string query = @"
        SELECT 
            a.Arendenummer,
            s.Beskrivning AS Stodform,
            u.UtbetID,
            u.UtbetDatum,
            u.UtbetStatus,
            SUM((utd.sluttid - utd.startTid + 1) * b.Belopp) AS Summa
        FROM Utbetalning u
        JOIN Utbetalningsplan up 
            ON u.UtbetPlanID = up.UtbetPlanID
        JOIN Arende a
            ON up.Arendenummer = a.Arendenummer
        JOIN Stodform s
            ON a.Stodformskod = s.Stodformskod
        JOIN UtbetaldTid utd
            ON u.UtbetID = utd.UtbetID
        JOIN UtbetaldTid_Belopp utb  
            ON utd.UtbetTidID = utb.UtbetaldTidID
        JOIN Belopp b
            ON utb.BeloppID = b.BeloppID
        GROUP BY a.Arendenummer, u.UtbetID, u.UtbetDatum, u.UtbetStatus, s.Beskrivning
        ORDER BY a.Arendenummer, u.UtbetDatum;
    ";

    XElement rawData = SQLResult(query, "UtbetalningarData", "Row").Result;

    var utbetalningarGruppade =
        from row in rawData.Elements("Row")
        let arendenummer = (string)row.Element("Arendenummer")
        let stodform = (string)row.Element("Stodform")
        let utbetID = (string)row.Element("UtbetID")
        let utbetDatum = (string)row.Element("UtbetDatum")
        let utbetStatus = (string)row.Element("UtbetStatus")
        let summa = int.Parse((string)row.Element("Summa") ?? "0") // Null-säkring

        group summa by new 
        {
            Arendenummer = arendenummer,
            Stodform = stodform,
            UtbetID = utbetID,
            UtbetDatum = utbetDatum,
            UtbetStatus = utbetStatus
        }
        into g
        select new
        {
            g.Key.Arendenummer,
            g.Key.Stodform,
            g.Key.UtbetID,
            g.Key.UtbetDatum,
            g.Key.UtbetStatus,
            Summa = g.Sum()
        };

    var arendenGruppade =
        from u in utbetalningarGruppade
        group u by new { u.Arendenummer, u.Stodform } into g
        select new
        {
            Arendenummer = g.Key.Arendenummer,
            Stodform = g.Key.Stodform,
            Utbetalningar = g,
            TotalSum = g.Sum(x => x.Summa),
            PaidSum = g.Where(x => x.UtbetStatus == "Utbetald").Sum(x => x.Summa)
        };

    var arendenMedLopnummer = arendenGruppade
        .OrderBy(a => a.Arendenummer)
        .Select((a, index) => new
        {
            a.Arendenummer,
            Lopnummer = index + 1,
            a.Stodform,
            a.Utbetalningar,
            a.TotalSum,
            a.PaidSum
        });

    XElement resultXml = new XElement("Arenden",
        from a in arendenMedLopnummer
        select new XElement("Arende",
            new XElement("Arendenummer", a.Arendenummer),
            new XElement("Lopnummer", a.Lopnummer),
            new XElement("Stodform", a.Stodform),
            new XElement("TotalSum", a.TotalSum),
            new XElement("PaidSum", a.PaidSum),
            new XElement("RemainingSum", a.TotalSum - a.PaidSum),
            new XElement("Utbetalningar",
                from u in a.Utbetalningar
                select new XElement("Utbetalning",
                    new XElement("UtbetDatum",  u.UtbetDatum),
                    new XElement("UtbetStatus", u.UtbetStatus),
                    new XElement("Summa",       u.Summa)
                )
            )
        )
    );

    return View(resultXml);
}



        //
        // GET: /Csn/Uppgift2
public ActionResult Uppgift2()
{
    string query = @"
        SELECT 
            u.UtbetDatum,
            btyp.Beskrivning AS Beloppstyp,
            SUM((utd.sluttid - utd.startTid + 1) * b.Belopp) AS Summa
        FROM Utbetalning u
        JOIN Utbetalningsplan up 
            ON u.UtbetPlanID = up.UtbetPlanID
        JOIN Arende a
            ON up.Arendenummer = a.Arendenummer
        JOIN UtbetaldTid utd
            ON u.UtbetID = utd.UtbetID
        JOIN UtbetaldTid_Belopp utb  
            ON utd.UtbetTidID = utb.UtbetaldTidID
        JOIN Belopp b
            ON utb.BeloppID = b.BeloppID
        JOIN Beloppstyp btyp 
            ON b.Beloppstypkod = btyp.Beloppstypkod
        GROUP BY u.UtbetDatum, btyp.Beskrivning
        ORDER BY u.UtbetDatum, btyp.Beskrivning;
    ";

    // Hämta SQL-resultatet som XML
    XElement rawData = SQLResult(query, "UtbetalningarData", "Row").Result;

    // Gruppera data på utbetalningsdatum
    var utbetalningarGruppade =
        from row in rawData.Elements("Row")
        let utbetDatum = row.Element("UtbetDatum")?.Value ?? "Okänt"
        let beloppstyp = row.Element("Beloppstyp")?.Value ?? "Okänt"
        let summa = int.TryParse(row.Element("Summa")?.Value, out int s) ? s : 0
        group new { beloppstyp, summa } by utbetDatum into g
        select new
        {
            UtbetDatum = g.Key,
            TotalSum = g.Sum(x => x.summa),
            Bidragstyper = g.OrderBy(x => x.beloppstyp)
                            .GroupBy(x => x.beloppstyp)
                            .Select(b => new { Beloppstyp = b.Key, Summa = b.Sum(x => x.summa) })
                            .ToList()
        };

    // Konvertera till XML
    XElement resultXml = new XElement("Utbetalningar",
        from u in utbetalningarGruppade
        select new XElement("Utbetalning",
            new XElement("Datum", u.UtbetDatum),
            new XElement("TotalSum", u.TotalSum),
            new XElement("Bidragstyper",
                from b in u.Bidragstyper
                select new XElement("Bidrag",
                    new XElement("Beloppstyp", b.Beloppstyp),
                    new XElement("Summa", b.Summa)
                )
            )
        )
    );

    return View(resultXml);
}


        //
        // GET: /Csn/Uppgift3
public ActionResult Uppgift3()
{
    string query = @"
        SELECT 
            a.Arendenummer,
            bt.Starttid,
            bt.Sluttid,
            s.Beskrivning AS Stodform,
            SUM((bt.Sluttid - bt.Starttid + 1) * b.Belopp) AS Summa
        FROM BeviljadTid bt
        JOIN Arende a 
            ON bt.Arendenummer = a.Arendenummer
        JOIN Stodform s 
            ON a.Stodformskod = s.Stodformskod
        JOIN BeviljadTid_Belopp btb 
            ON bt.BeviljadTidID = btb.BeviljadTidID
        JOIN Belopp b
            ON btb.BeloppID = b.BeloppID
        GROUP BY a.Arendenummer, bt.Starttid, bt.Sluttid, s.Beskrivning
        ORDER BY a.Arendenummer, bt.Starttid;
    ";

    // Hämta SQL-resultatet som XML
    XElement rawData = SQLResult(query, "BeviljadeTiderData", "Row").Result;

    // Gruppera data på ärendenummer
    var beviljadeTider =
        from row in rawData.Elements("Row")
        let arendenummer = row.Element("Arendenummer")?.Value ?? "Okänt"
        let starttid = row.Element("Starttid")?.Value ?? "0"
        let sluttid = row.Element("Sluttid")?.Value ?? "0"
        let stodform = row.Element("Stodform")?.Value ?? "Okänt"
        let summa = int.TryParse(row.Element("Summa")?.Value, out int s) ? s : 0
        group new { starttid, sluttid, stodform, summa } by arendenummer into g
        select new
        {
            Arendenummer = g.Key,
            Perioder = g.Select(p => new
            {
                Starttid = p.starttid,
                Sluttid = p.sluttid,
                Stodform = p.stodform,
                Summa = p.summa
            }).ToList()
        };

    // Konvertera till XML
    XElement resultXml = new XElement("BeviljadeTider",
        from arende in beviljadeTider
        select new XElement("Arende",
            new XElement("Arendenummer", arende.Arendenummer),
            from period in arende.Perioder
            select new XElement("Period",
                new XElement("Tidsintervall", $"{period.Starttid} - {period.Sluttid}"),
                new XElement("Stodform", period.Stodform),
                new XElement("Summa", period.Summa)
            )
        )
    );

    return View(resultXml);
}

    }
}
