using System.Text;
using Inetlab.SMPP.Common;

namespace TravelAd_Api.Models
{
    public class SmsModel
    {
        //public class SmppConnection
        //{
        //    public int ChannelId { get; set; }
        //    public string Host { get; set; }
        //    public int Port { get; set; }
        //    public string SystemId { get; set; }
        //    public string Password { get; set; }
        //    public string AddressRange { get; set; } = ""; // Optional

        //    // NEW: TON & NPI for Binding
        //    public byte BindingTON { get; set; } = 0; // Default Unknown
        //    public byte BindingNPI { get; set; } = 0; // Default Unknown
        //}

        //public class SendSmsRequest
        //{
        //    public int ChannelId { get; set; }
        //    public string Sender { get; set; }
        //    public string Receiver { get; set; }
        //    public string Message { get; set; }

        //    // NEW: TON & NPI for Sender and Receiver
        //    public byte SenderTON { get; set; } = 5;  // Default Alphanumeric
        //    public byte SenderNPI { get; set; } = 0;  // Default Unknown
        //    public byte ReceiverTON { get; set; } = 1; // Default International/National
        //    public byte ReceiverNPI { get; set; } = 1; // Default E.164 standard
        //}

        //public class SendBulkSmsRequest
        //{
        //    public int ChannelId { get; set; }
        //    public string Sender { get; set; }
        //    public List<string> Recipients { get; set; }
        //    public string Message { get; set; }

        //    // NEW: TON & NPI for Sender and Recipients
        //    public byte SenderTON { get; set; } = 5;  // Default Alphanumeric
        //    public byte SenderNPI { get; set; } = 0;  // Default Unknown
        //    public byte ReceiverTON { get; set; } = 1; // Default International/National
        //    public byte ReceiverNPI { get; set; } = 1; // Default E.164 standard
        //}


        public class StatusBody
        {
            public string Status { get; set; }
            public string Status_Description { get; set; }
            public string DeliveryStatus { get; set; }
            public int? channel_id { get; set; }
        }

        public class CreateSmppChannel
        {
            public string ChannelName { get; set; }
            public string Type { get; set; }

            public string Host { get; set; }
            public int Port { get; set; }
            public string SystemId { get; set; }
            public string Password { get; set; }



        }

        public class CreateSMSServer
        {
            public string ServerName { get; set; }

            public string ServerType { get; set; }
            public string ServerUrl { get; set; }

        }

        public class SmppConnectionRequest

        {

            public string ChannelName { get; set; }
            public string Type { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public string SystemId { get; set; }
            public string Password { get; set; }
            public int ServerId { get; set; }
            public string ShortCodes { get; set; }

            // NEW: TON & NPI Fields
            public int BindingTON { get; set; }
            public int BindingNPI { get; set; }
            public int SenderTON { get; set; }
            public int SenderNPI { get; set; }
            public int DestinationTON { get; set; }
            public int DestinationNPI { get; set; }
            public string TransportProtocol { get; set; }
            public int BindMode { get; set; }
            public string AddressRange { get; set; }
            public string SystemType { get; set; }
            public int DataCodingScheme { get; set; }
            public string CharacterEncoding { get; set; }

            public string ServiceType { get; set; }
        }





        public class SmppConnection
        {
            public int ChannelId { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public string SystemId { get; set; }
            public string Password { get; set; }
            public ConnectionMode BindMode { get; set; }
            public string SystemType { get; set; }
            public string AddressRange { get; set; }
            public byte BindingTON { get; set; }
            public byte BindingNPI { get; set; }
            //public string ServiceType { get; set; }
            //public DataCodings DataCoding { get; set; }
            //public Encoding CharacterEncoding { get; set; }
            public bool UseSSL { get; set; } = false;
        }

        public class SendSmsRequest
        {
            public int ChannelId { get; set; }
            public string Sender { get; set; }
            public byte SenderTON { get; set; }
            public byte SenderNPI { get; set; }
            public string Receiver { get; set; }
            public byte DestinationTON { get; set; }
            public byte DestinationNPI { get; set; }
            public string Message { get; set; }
            public string ServiceType { get; set; }
            public DataCodings DataCoding { get; set; }
            public string CharacterEncoding { get; set; }
        }

        public class SendBulkSmsRequest
        {
            public int ChannelId { get; set; }
            public string Sender { get; set; }
            public AddressTON SenderTON { get; set; }
            public AddressNPI SenderNPI { get; set; }
            public List<string> Recipients { get; set; }
            public AddressTON DestinationTON { get; set; }
            public AddressNPI DestinationNPI { get; set; }
            public string Message { get; set; }
            public DataCodings DataCoding { get; set; }
            public Encoding CharacterEncoding { get; set; }
        }

        public class AdvancedSMSData
        {
            public int senderTON { get; set; }
            public int senderNPI { get; set; }
            public int receiverTON { get; set; }
            public int receiverNPI { get; set; }
            public string serviceType { get; set; }
            public int dataEncoding { get; set; }
            public string characterEncoding { get; set; }
        }

        public class failureCase { 
        public int count { get; set; }
        }

    }
}
