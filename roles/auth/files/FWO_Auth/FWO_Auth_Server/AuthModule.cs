using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FWO_Auth_Server;

namespace FWO_Auth
{
    public class AuthModule
    {
        private readonly HttpListener Listener;
        private readonly Ldap LdapConnection;
        private readonly TokenGenerator TokenGenerator;

        private readonly string privateKey = "8f4ce02dabb2a4ffdb2137802b82d1283f297d959604451fd7b7287aa307dd298668cd68a432434d85f9bcff207311a833dd5b522870baf457c565c7a716e7eaf6be9a32bd9cd5420a0ebaa9bace623b54c262dcdf35debdb5388490008b9bc61facfd237c1c7058f5287881a37492f523992a2a120a497771954daf27666de2461a63117c8347fe760464e3a58b3a5151af56a0375c8b34921101c91425b65097fc69049f85589a58bb5e5570139c98d3edb179a400b3d142a30e32d1c8e9bbdb90d799fb81b4fa6fb7751acfb3529c7af022590cbb845a8390b906f725f079967b269cff8d2e6c8dbcc561b37c4bdd1928c662b79f42fe56fe108a0cf21e08";
        //private readonly string privateKey = "769d910a91f5ccce38cecf976d04c47bb8906160c359936e3c321ee0f3d496009190a4ddb81d79934d15291d2e0b0ecd5f43122acb4deea0d5f52d657a44d9aa50dc6145b969d0f6ed7ab9f161f80b7dfcb158104d3097f17b487190ac18d71f3b1fa92c2862f238360ae955ab626b278763c7ae889350624532ccc07fd7ada256af826fcf6f8df91f400aca67c267afb4dc6df689a2c20f280d85cb99d9cb44615d96ecdb4a215e69403b2f1c350112b6cb8333c87b59e98f16f2748bab1ca74ca808cf1c7bf320c4914c767e40e0bc4dffef05c6b28794a73d67ee09ef9b55be2ec0d2b5e0e5a548582ae095a36245c433371a560c7e4cf0011dfd657a708e";
        private readonly int daysValid = 7;

        public AuthModule()
        {
            // TODO: Get Ldap Server URI + Http Listener URI from config file

            // Get private key from
            // absolute path: "fworch_home/etc/secrets/jwt_private.key"
            // absolute path: "fworch_home/etc/secrets/jwt_private.key"
            // relative path:  "../../../etc/secrets"

            try
            {
                // privateKey = File.ReadAllText("../../../etc/secrets/jwt_private.key");
                // TODO: Read as relative path
                privateKey = File.ReadAllText("/usr/local/fworch/etc/secrets/jwt_private.key").TrimEnd();
                Console.WriteLine($"Key is {privateKey.Length} Bytes long.");
                Console.WriteLine($"Key is {privateKey}");
            }
            catch (Exception e)
            {
                ConsoleColor StandardConsoleColor = Console.ForegroundColor;

                Console.ForegroundColor = ConsoleColor.Red;

                Console.Out.WriteAsync($"Error while trying to read private key : \n Message \n ### \n {e.Message} \n ### \n StackTrace \n ### \n {e.StackTrace} \n ### \n");
                Console.Out.WriteAsync($"Using fallback key! \n");

                Console.ForegroundColor = StandardConsoleColor;
            }

            // Create Http Listener
            Listener = new HttpListener();

            // Create connection to Ldap Server
            LdapConnection = new Ldap("localhost", 636); // todo: read ldap listener address from config

            // Create Token Generator
            TokenGenerator = new TokenGenerator(privateKey, daysValid);

            // Start Http Listener
            StartListener("http://localhost:8888/"); // todo: read auth server listener address from config
        }

        private void StartListener(string ListenerUri)
        {
            // Add prefixes to listen to 
            Listener.Prefixes.Add(ListenerUri + "jwt/");

            // Start listener.
            Listener.Start();
            Console.WriteLine("Listening...");

            while (true)
            {
                // Note: The GetContext method blocks while waiting for a request.
                HttpListenerContext context = Listener.GetContext();

                HttpListenerRequest request = context.Request;
                HttpStatusCode status = HttpStatusCode.OK;

                string responseString = "";

                try
                {
                    switch (request.Url.LocalPath)
                    {
                        case "/jwt":
                            responseString = CreateJwt(request);
                            break;

                        default:
                            status = HttpStatusCode.InternalServerError;
                            break;
                    }
                }

                catch (Exception e)
                {
                    status = HttpStatusCode.BadRequest;

                    Console.WriteLine($"Error {e.Message}    Stacktrace  {e.StackTrace}");
                    // Todo: Log error
                }

                // Get response object.
                HttpListenerResponse response = context.Response;

                // Get response stream from response object
                Stream output = response.OutputStream;

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);              
                
                response.StatusCode = (int)status;
                response.ContentLength64 = buffer.Length;  
                
                output.Write(buffer, 0, buffer.Length);

                output.Close();
            }
        }

        private string CreateJwt(HttpListenerRequest request)
        {
            string responseString = "";

            if (request.HttpMethod == HttpMethod.Post.Method)
            {
                string ParametersJson = new StreamReader(request.InputStream).ReadToEnd();
                Dictionary<string, string> Parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(ParametersJson);

                User User = new User { Name = Parameters["Username"], Password = Parameters["Password"] };

                // TODO: REMOVE LATER
                if (User.Name == "" && User.Password == "")
                {
                    Console.WriteLine("Logging in with fake user...");
                    responseString = TokenGenerator.CreateJWT(User, null, LdapConnection.GetRoles(User));                 
                }
                    
                // REMOVE LATER                             

                else
                {
                    Console.WriteLine($"Try to validate as {User.Name}...");
                    String UserDN = LdapConnection.ValidateUser(User);
                    if (UserDN!="") 
                    {   // user was successfully auhtenticated via LDAP
                        Console.WriteLine($"Successfully validated as {User} with DN {UserDN}");
                        // User.UserDN = UserDN;

                        Tenant tenant = new Tenant();
                        tenant.tenantName = UserDN; //only part of it (first ou)

                        // need to make APICalls available as common library

                        // need to resolve tenant_name from DN to tenant_id first 
                        // query get_tenant_id($tenant_name: String) { tenant(where: {tenant_name: {_eq: $tenant_name}}) { tenant_id } }
                        // variables: {"tenant_name": "forti"}
                        tenant.tenantId = 0; // todo: replace with APICall() result

                        // get visible devices with the following queries:

                        // query get_visible_mgm_per_tenant($tenant_id:Int!){  get_visible_managements_per_tenant(args: {arg_1: $tenant_id})  id } }
                        String variables = $"\"tenant_id\":{tenant.tenantId}";
                        // tenant.VisibleDevices = APICall(query,variables);

                        // query get_visible_devices_per_tenant($tenant_id:Int!){ get_visible_devices_per_tenant(args: {arg_1: $tenant_id}) { id }}
                        // variables: {"tenant_id":3}
                        // tenant.VisibleDevices = APICall();
            
                        // tenantInformation.VisibleDevices = {};
                        // tenantInformation.VisibleManagements = [];

                        UserData userData = new UserData();
                        userData.tenant = tenant;
                        responseString = TokenGenerator.CreateJWT(User, userData, LdapConnection.GetRoles(User));
                    }

                    else
                    {
                        responseString = "InvalidCredentials";
                    }
                }                   
            }

            return responseString;
        }
    }
}