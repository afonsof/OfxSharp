using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Sgml;

namespace OfxSharpLib
{
    public class OfxDocumentParser
    {
        public OfxDocument Import(Stream stream, Encoding encoding)
        {
            using (var reader = new StreamReader(stream, encoding))
            {
                return Import(reader.ReadToEnd());
            }
        }

        public OfxDocument Import(Stream stream)
        {
            return Import(stream, Encoding.Default);
        }

        public OfxDocument Import(string ofx)
        {
            return ParseOfxDocument(ofx);
        }

        private OfxDocument ParseOfxDocument(string ofxString)
        {
            //If OFX file in SGML format, convert to XML
            if (!IsXmlVersion(ofxString))
            {
                ofxString = SgmltoXml(ofxString);
            }

            return Parse(ofxString);
        }

        private OfxDocument Parse(string ofxString)
        {
            var ofx = new OfxDocument { AccType = GetAccountType(ofxString) };

            //Load into xml document
            var doc = new XmlDocument();
            doc.Load(new StringReader(ofxString));

            var currencyNode = doc.SelectSingleNode(GetXPath(ofx.AccType, OfxSection.Currency));

            if (currencyNode != null)
            {
                ofx.Currency = currencyNode.FirstChild.Value;
            }
            else
            {
                throw new OfxParseException("Currency not found");
            }

            //Get sign on node from OFX file
            var signOnNode = doc.SelectSingleNode(Resources.SignOn);

            //If exists, populate signon obj, else throw parse error
            if (signOnNode != null)
            {
                ofx.SignOn = new SignOn(signOnNode);
            }
            else
            {
                throw new OfxParseException("Sign On information not found");
            }

            //Get Account information for ofx xmlDocument
            var accountNode = doc.SelectSingleNode(GetXPath(ofx.AccType, OfxSection.AccountInfo));

            //If account info present, populate account object
            if (accountNode != null)
            {
                ofx.Account = new Account(accountNode, ofx.AccType);
            }
            else
            {
                throw new OfxParseException("Account information not found");
            }

            //Get list of transactions
            ImportTransations(ofx, doc);

            //Get balance info from ofx xmlDocument
            var ledgerNode = doc.SelectSingleNode(GetXPath(ofx.AccType, OfxSection.Balance) + "/LEDGERBAL");
            var avaliableNode = doc.SelectSingleNode(GetXPath(ofx.AccType, OfxSection.Balance) + "/AVAILBAL");

            //If balance info present, populate balance object
            // ***** OFX files from my bank don't have the 'avaliableNode' node, so i manage a 'null' situation
            if (ledgerNode != null) // && avaliableNode != null
            {
                ofx.Balance = new Balance(ledgerNode, avaliableNode);
            }
            else
            {
                throw new OfxParseException("Balance information not found");
            }

            return ofx;
        }


        /// <summary>
        /// Returns the correct xpath to specified section for given account type
        /// </summary>
        /// <param name="type">Account type</param>
        /// <param name="section">Section of OFX document, e.g. Transaction Section</param>
        /// <exception cref="OfxException">Thrown in account type not supported</exception>
        private string GetXPath(AccountType type, OfxSection section)
        {
            string xpath, accountInfo;

            switch (type)
            {
                case AccountType.Bank:
                    xpath = Resources.BankAccount;
                    accountInfo = "/BANKACCTFROM";
                    break;
                case AccountType.Cc:
                    xpath = Resources.CCAccount;
                    accountInfo = "/CCACCTFROM";
                    break;
                default:
                    throw new OfxException("Account Type not supported. Account type " + type);
            }

            switch (section)
            {
                case OfxSection.AccountInfo:
                    return xpath + accountInfo;
                case OfxSection.Balance:
                    return xpath;
                case OfxSection.Transactions:
                    return xpath + "/BANKTRANLIST";
                case OfxSection.Signon:
                    return Resources.SignOn;
                case OfxSection.Currency:
                    return xpath + "/CURDEF";
                default:
                    throw new OfxException("Unknown section found when retrieving XPath. Section " + section);
            }
        }

        /// <summary>
        /// Returns list of all transactions in OFX document
        /// </summary>
        /// <param name="ofxDocument">OFX Document</param>
        /// <param name="xmlDocument">XML Document</param>
        /// <returns>List of transactions found in OFX document</returns>
        private void ImportTransations(OfxDocument ofxDocument, XmlNode xmlDocument)
        {
            var xpath = GetXPath(ofxDocument.AccType, OfxSection.Transactions);

            ofxDocument.StatementStart = xmlDocument.GetValue(xpath + "//DTSTART").ToDate();
            ofxDocument.StatementEnd = xmlDocument.GetValue(xpath + "//DTEND").ToDate();

            var transactionNodes = xmlDocument.SelectNodes(xpath + "//STMTTRN");
            ofxDocument.Transactions = new List<Transaction>();

            if (transactionNodes == null) return;
            foreach (XmlNode node in transactionNodes)
            {
                ofxDocument.Transactions.Add(new Transaction(node, ofxDocument.Currency));
            }
        }


        /// <summary>
        /// Checks account type of supplied file
        /// </summary>
        /// <param name="file">OFX file want to check</param>
        /// <returns>Account type for account supplied in ofx file</returns>
        private AccountType GetAccountType(string file)
        {
            if (file.IndexOf("<CREDITCARDMSGSRSV1>", StringComparison.Ordinal) != -1)
                return AccountType.Cc;

            if (file.IndexOf("<BANKMSGSRSV1>", StringComparison.Ordinal) != -1)
                return AccountType.Bank;

            throw new OfxException("Unsupported Account Type");
        }

        /// <summary>
        /// Check if OFX file is in SGML or XML format
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        private bool IsXmlVersion(string file)
        {
            return (file.IndexOf("OFXHEADER:100", StringComparison.Ordinal) == -1);
        }

        /// <summary>
        /// Converts SGML to XML
        /// </summary>
        /// <param name="file">OFX File (SGML Format)</param>
        /// <returns>OFX File in XML format</returns>
        private string SgmltoXml(string file)
        {
            var reader = new SgmlReader
            {
                InputStream = new StringReader(ParseHeader(file)),
                DocType = "OFX"
            };

            var sw = new StringWriter();
            var xml = new XmlTextWriter(sw);

            //write output of sgml reader to xml text writer
            while (!reader.EOF)
                xml.WriteNode(reader, true);

            //close xml text writer
            xml.Flush();
            xml.Close();

            var temp = sw.ToString().TrimStart().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            return String.Join("", temp);
        }

        /// <summary>
        /// Checks that the file is supported by checking the header. Removes the header.
        /// </summary>
        /// <param name="file">OFX file</param>
        /// <returns>File, without the header</returns>
        private string ParseHeader(string file)
        {
            //Select header of file and split into array
            //End of header worked out by finding first instance of '<'
            //Array split based of new line & carrige return
            var header = file.Substring(0, file.IndexOf('<'))
               .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            //Check that no errors in header
            CheckHeader(header);

            //Remove header
            return file.Substring(file.IndexOf('<') - 1);
        }

        /// <summary>
        /// Checks that all the elements in the header are supported
        /// </summary>
        /// <param name="header">Header of OFX file in array</param>
        private void CheckHeader(string[] header)
        {
            if (header[0] != "OFXHEADER:100")
                throw new OfxParseException("Incorrect header format");

            if (header[1] != "DATA:OFXSGML")
                throw new OfxParseException("Data type unsupported: " + header[1] + ". OFXSGML required");

            if (header[2] != "VERSION:102")
                throw new OfxParseException("OFX version unsupported. " + header[2]);

            if (header[3] != "SECURITY:NONE")
                throw new OfxParseException("OFX security unsupported");

            if (header[4] != "ENCODING:USASCII" && header[4] != "ENCODING:UTF-8")
                throw new OfxParseException("ENCODING unsupported:" + header[4]);

            if (header[5] != "CHARSET:1252" && header[5] != "CHARSET:NONE")
                throw new OfxParseException("Charecter set unsupported:" + header[5]);

            if (header[6] != "COMPRESSION:NONE")
                throw new OfxParseException("Compression unsupported");

            if (header[7] != "OLDFILEUID:NONE")
                throw new OfxParseException("OLDFILEUID incorrect");
        }

        #region Nested type: OFXSection

        /// <summary>
        /// Section of OFX Document
        /// </summary>
        private enum OfxSection
        {
            Signon,
            AccountInfo,
            Transactions,
            Balance,
            Currency
        }

        #endregion
    }
}