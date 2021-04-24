using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CebelcaAPI
{
  public class CebelcaPartner
  {
    public string Id { get; set; }
    public string Name { get; set; }
  }
  public class CebelcaAPISharp
  {
    private string _key = "";
    public CebelcaAPISharp(string key)
    {
      _key = key;
    }
    private async Task<string> APICall(string region, string method, Dictionary<string, string> postvalues)
    {
      using (var client = new HttpClient())
      {
        var byteArray = Encoding.ASCII.GetBytes($"{_key}:x");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        //  var url = "https://www.cebelca.biz/API?_r=invoice-sent&_m=insert-into";
        var url = $"https://www.cebelca.biz/API?_r={region}&_m={method}";
        var content = new FormUrlEncodedContent(postvalues);
        var response = await client.PostAsync(url, content);

        var responseString = await response.Content.ReadAsStringAsync();
        return responseString;

      }
    }

    public async Task<string> AddInvoiceHead(string partnerId, string idDocumentExt, DateTime dateSent, DateTime dateServed, DateTime dateToPay, bool paid = false)
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {
                { "date_sent",dateSent.ToShortDateString() },
                { "date_served",dateServed.ToShortDateString() },
                { "date_to_pay", dateToPay.ToShortDateString()  },
                { "id_partner", partnerId },
                { "id_document_ext", idDocumentExt }
            };
      if (paid)
      {
        values.Add("payment", "paid");
        values.Add("payment_act", "1");
      }
      var ret = await APICall("invoice-sent", "insert-smart-2", values);
      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "id")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["id"].Value<string>();
      return id;

    }

    public async Task<IEnumerable<CebelcaPartner>> GetPartners()
    {
      var values = new Dictionary<string, string>();
      var ret = await APICall("partner", "select-all", values);

      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "id")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["id"].Value<string>();
      //var l = new List<CebelcaPartner>();
      var l = json[0].Select(x => new CebelcaPartner
      {
        Id = x["id"].Value<string>(),
        Name = x["name"].Value<string>()
      }).ToList();
      return l;
    }

    public async Task SendInvoiceByEmail(string invoiceId, string to, string subject, string content)
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {
                { "id_invoice_sent", invoiceId},
                { "mto", to},
                { "msubj", subject },
                { "docformat", "pdf"},
                { "doctitle", "Račun št."},
                { "lang", "si"},
                { "content", content},
                { "format", "pdf"}
            };
      var ret = await APICall("mailer", "push-invoice-sent-doc", values);

    }



    public async Task<string> AddInvoiceLine(string invoiceId, string title, string measuringUnit, string qty, decimal price, string vat, string discount)
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {
                { "title",title },
                { "mu",measuringUnit },
                { "qty",qty },
                { "id_invoice_sent", invoiceId },
                { "price", price.ToString() },
                { "vat", vat }
            };
      var ret = await APICall("invoice-sent-b", "insert-into", values);
      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "id")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["id"].Value<string>();
      return id;

    }

    public async Task<string> AddPayment(string invoiceId, DateTime dateOfPayment, decimal amount, string paymentMethod)
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {
                { "date_of",dateOfPayment.ToShortDateString() },
                { "amount",amount.ToString() },

                { "id_invoice_sent", invoiceId },
                { "id_payment_method", paymentMethod},

            };
      var ret = await APICall("invoice-sent-p", "insert-into", values);
      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "id")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["id"].Value<string>();
      return id;

    }

    public async Task<string> IssueInvoiceNoFiscalization(string invoiceId, string docType = "0")
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {

                { "id", invoiceId },
                { "doctype", docType},

            };
      var ret = await APICall("invoice-sent", "finalize-invoice-2015", values);
      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "new_title")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["new_title"].Value<string>();
      return id;

    }

    public async Task<string> IssueInvoiceFiscalization(string invoiceId, string idLocation, string opTaxId, string opName, bool test_mode = false)
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {

                { "id", invoiceId },
                { "id_location", idLocation },
                { "op-tax-id", opTaxId },
                { "op-name", opName },
                { "fiscalize", "1" },
                { "test_mode", test_mode ? "1" : "0" },

            };
      var ret = await APICall("invoice-sent", "finalize-invoice", values);
      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "docnum")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["docnum"].Value<string>();
      var eor = json[0][0]["eor"].Value<string>();
      return id;

    }

    public async Task<string> AddPartner(string name, string email, string street, string city, 
      string postal)
    {
      Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
      var values = new Dictionary<string, string>
            {

                { "name", name },
                { "email", email },
                { "street", street },
                { "city", city },
                { "postal", postal },
                

            };
      var ret = await APICall("partner", "assure", values);
      var json = JArray.Parse(ret);
      var retname = (json[0][0] as JObject).Properties().First().Name;
      if (retname != "id")
        throw new Exception("Error from api: " + ret);
      var id = json[0][0]["id"].Value<string>();
      return id;

    }

  }
}
