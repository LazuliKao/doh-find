#r "nuget: Ae.Dns.Client, 3.0.0"
#r "nuget: JsonhCs, 3.2.0"

open System
open System.Net.Http
open Ae.Dns.Client
open Ae.Dns.Protocol
open System.Diagnostics

type DnsServerList =
    { cn: string array
      ``global``: string array }

let dnsServerList =
    use json = IO.File.OpenRead "list.json"
    let element = JsonhCs.JsonhReader.ParseElement<DnsServerList> json
    element.Value

let availableDnsServers =
    Array.concat [ dnsServerList.cn; dnsServerList.``global`` ] |> Array.distinct

let query address dnsServer =
    task {
        use httpClient = new HttpClient(Timeout = TimeSpan.FromSeconds 20.0)
        httpClient.BaseAddress <- Uri dnsServer
        use dnsClient = new DnsHttpClient(httpClient) :> IDnsClient
        let query = DnsQueryFactory.CreateQuery address
        let! answer = dnsClient.Query query
        return answer
    }

let queryEvaluate dnsServer address =
    task {
        let sw = Stopwatch.StartNew()
        let! result = query dnsServer address
        let mutable timelist = [ sw.ElapsedMilliseconds ]
        let times = 5

        for i in 1..times do
            let! result = query dnsServer address
            timelist <- timelist @ [ sw.ElapsedMilliseconds ]

        return result, timelist |> List.map (fun x -> double x) |> Seq.average |> int64
    }
// https://www.gstatic.com/ipranges/goog.json
open System.Net

type GooglePrefix =
    { ipv4Prefix: string option
      ipv6Prefix: string option }

type GoogleRangeJson =
    { syncToken: string
      creationTime: string
      prefixes: GooglePrefix[] }

type IpRange(ip: IPAddress, mask: int) =
    member _.Ip = ip
    member _.Mask = mask

    member _.Contains(target: IPAddress) =
        if ip.AddressFamily <> target.AddressFamily then
            false
        else
            let ipBytes = ip.GetAddressBytes()
            let targetBytes = target.GetAddressBytes()

            let maskBytes =
                let totalBits = mask
                let mutable bitsLeft = totalBits

                ipBytes
                |> Array.map (fun _ ->
                    if bitsLeft >= 8 then
                        bitsLeft <- bitsLeft - 8
                        0xFFuy
                    elif bitsLeft > 0 then
                        let v = byte (0xFF00us >>> bitsLeft &&& 0xFFus)
                        bitsLeft <- 0
                        v
                    else
                        0uy)

            let maskedBase = Array.map2 (fun b m -> b &&& m) ipBytes maskBytes
            let maskedTarget = Array.map2 (fun b m -> b &&& m) targetBytes maskBytes
            maskedBase = maskedTarget

let googleRangeJson =
    task {
        // https://support.google.com/a/answer/10026322?hl=zh-Hans
        let file = "goog.json"

        let! json =
            task {
                if IO.File.Exists file then
                    let! json = IO.File.ReadAllTextAsync file |> Async.AwaitTask
                    return json
                else
                    use httpClient = new HttpClient(Timeout = TimeSpan.FromSeconds 20.0)

                    let! response = httpClient.GetStringAsync "https://www.gstatic.com/ipranges/goog.json"
                    return response
            }

        IO.File.WriteAllText(file, json)
        let data = Text.Json.JsonSerializer.Deserialize<GoogleRangeJson> json

        return
            data.prefixes
            |> Array.choose (fun x -> x.ipv4Prefix)
            |> Array.map (fun x -> x.Split '/')
            |> Array.map (fun x -> x.[0], int x.[1])
            |> Array.map (fun (ip, mask) -> IpRange(IPAddress.Parse ip, mask))


    }

let isInGoogleRange (ip: string) =
    let ipv4Prefix = googleRangeJson |> Async.AwaitTask |> Async.RunSynchronously
    let ip = System.Net.IPAddress.Parse ip
    let ipRange = ipv4Prefix |> Array.tryFind (fun x -> x.Contains ip)

    match ipRange with
    | Some _ -> true
    | None -> false

type IpApiResponse =
    { ip: string
      network: string
      version: string
      city: string
      region: string
      region_code: string
      country: string
      country_name: string
      country_code: string
      country_code_iso3: string
      country_capital: string
      country_tld: string
      continent_code: string
      in_eu: bool
      postal: string option
      latitude: double
      longitude: double
      timezone: string
      utc_offset: string
      country_calling_code: string
      currency: string
      currency_name: string
      languages: string
      country_area: double
      country_population: double
      asn: string
      org: string }

let getIpLocation (ip: string) =
    task {
        use httpClient = new HttpClient(Timeout = TimeSpan.FromSeconds 20.0)
        let! response = httpClient.GetStringAsync $"https://ipapi.co/{ip}/json" |> Async.AwaitTask
        let data = Text.Json.JsonSerializer.Deserialize<IpApiResponse> response
        return sprintf "Location: %s, %s, %s ASN:%s(%s)" data.city data.region data.country data.asn data.org
    }

let main () =
    async {
        let availablensServers = Collections.Generic.List<string * int64 * string>()

        let! _ =
            [ for dnsServer in availableDnsServers do
                  async {
                      try
                          let address = "www.google.com"
                          let! result, times = queryEvaluate address dnsServer |> Async.AwaitTask
                          // for all
                          let isGoogle =
                              result.Answers |> Seq.forall (fun x -> isInGoogleRange (x.Resource.ToString()))

                          printfn
                              "[+] Average Time: %d ms | %s | Response: %A | Real %b"
                              times
                              dnsServer
                              result.Answers[0].Resource
                              isGoogle

                          if isGoogle then
                              availablensServers.Add(dnsServer, times, result.Answers[0].Resource.ToString())
                      with ex ->
                          printfn "[ ] %s | Some error happened: %s" dnsServer ex.Message
                  } ]
            |> fun tasks -> Async.Parallel(tasks, 8) // 限制最大并发数为8
            |> Async.Ignore

        printfn "Sorted DNS Servers:"

        for dnsServer, times, result in availablensServers |> Seq.sortBy (fun (_, t, _) -> t) do
            printfn "%d ms\t%s\t%s" times dnsServer result

            try
                let! location = getIpLocation result |> Async.AwaitTask
                printfn "\t %s" location
            with _ ->
                ()
    }

main () |> Async.RunSynchronously
