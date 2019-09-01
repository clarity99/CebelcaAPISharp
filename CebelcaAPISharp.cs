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

        public async Task<string> AddInvoiceHead(string partnerId, string idDocumentExt, DateTime dateSent, DateTime dateServed, DateTime dateToPay)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("sl-SI");
            var values = new Dictionary<string, string>
            {
                { "date_sent",dateSent.ToShortDateString() },
                { "date_served",dateServed.ToShortDateString() },
                { "date_to_pay",dateToPay.ToShortDateString() },
                { "id_partner", partnerId },
                { "id_document_ext", idDocumentExt }
            };
            var ret = await APICall("invoice-sent", "insert-smart-2", values);
            var json = JArray.Parse(ret);
            var retname = (json[0][0] as JObject).Properties().First().Name;
            if (retname != "id")
                throw new Exception("Error from api: "+ret);
            var id = json[0][0]["id"].Value<string>();
            return id;

        }

        public async Task<string> AddInvoiceLine(string invoiceId,string title, string measuringUnit, string qty, decimal price, string vat, string discount)
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

        public async Task<string> AddPayment(string invoiceId, DateTime dateOfPayment, decimal amount, string paymentMethod )
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

        public async Task<string> IssueInvoiceNoFiscalization(string invoiceId, string docType="0")
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

    }
}
