#r "nuget: Ae.Dns.Client, 3.0.0"

open System
open System.Net.Http
open Ae.Dns.Client
open Ae.Dns.Protocol
open System.Diagnostics

let availableDnsServers =
    [
      // category: China DNS
      "https://dns.alidns.com/dns-query"
      "https://223.5.5.5/dns-query"
      "https://223.6.6.6/dns-query"
      "https://doh.pub/dns-query"
      "https://1.12.12.12/dns-query"
      "https://120.53.53.53/dns-query"
      "https://doh.360.cn/dns-query"
      "https://doh.apad.pro/dns-query"
      // category: Public DNS
      "https://doh.sb/dns-query"
      "https://doh-jp.blahdns.com/dns-query"
      "https://doh.dns.sb/dns-query"
      "https://dns.google/dns-query"
      "https://jp.tiarap.org/dns-query"
      "https://dns.quad9.net/dns-query"
      "https://dns10.quad9.net/dns-query"
      "https://doh.cleanbrowsing.org/doh/security-filter/"
      "https://doh.opendns.com/dns-query"
      "https://cloudflare-dns.com/dns-query"
      "https://dns-unfiltered.adguard.com/dns-query"
      "https://1.1.1.1/dns-query"
      "https://1.0.0.1/dns-query"
      "https://9.9.9.9/dns-query"
      "https://149.112.112.112/dns-query"
      "https://max.rethinkdns.com/dns-query"
      "https://sky.rethinkdns.com/dns-query"
      "https://doh.opendns.com/dns-query"
      "https://dns.cloudflare.com/dns-query"
      "https://1.0.0.1/dns-query"
      "https://dns.bebasid.com/unfiltered"
      "https://0ms.dev/dns-query"
      "https://dns.decloudus.com/dns-query"
      "https://wikimedia-dns.org/dns-query"
      "https://doh.applied-privacy.net/query"
      "https://private.canadianshield.cira.ca/dns-query"
      "https://dns.controld.com/comss"
      "https://kaitain.restena.lu/dns-query"
      "https://doh.libredns.gr/dns-query"
      "https://doh.libredns.gr/ads"
      "https://dns.switch.ch/dns-query"
      "https://doh.nl.ahadns.net/dns-query"
      "https://doh.la.ahadns.net/dns-query"
      "https://dns.dnswarden.com/uncensored"
      "https://doh.ffmuc.net/dns-query"
      "https://dns.oszx.co/dns-query"
      "https://doh.tiarap.org/dns-query"
      "https://dns.adguard.com/dns-query" ]

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
        let file = "goog.json"

        let! json =
            async {
                if IO.File.Exists file then
                    let! json = IO.File.ReadAllTextAsync file |> Async.AwaitTask
                    return json
                else
                    use httpClient = new HttpClient(Timeout = TimeSpan.FromSeconds 20.0)

                    let! response =
                        httpClient.GetStringAsync "https://www.gstatic.com/ipranges/goog.json"
                        |> Async.AwaitTask

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
    task {
        let availablensServers = Collections.Generic.List<string * int64 * string>()

        let! _ =
            [ for dnsServer in availableDnsServers do
                  task {
                      try
                          let address = "www.google.com"
                          let! result, times = queryEvaluate address dnsServer
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
                          ()
                  //   printfn "[ ] %s | Some error happened: %s" dnsServer ex.Message
                  } ]
            |> Seq.map (fun x -> x |> Async.AwaitTask)
            |> Async.Parallel
            |> Async.Ignore

        printfn "Sorted DNS Servers:"

        for dnsServer, times, result in availablensServers |> Seq.sortBy (fun (_, t, _) -> t) do
            printfn "%d ms \t %s" times dnsServer

            try
                let! location = getIpLocation result
                printfn "\tA:%s  %s" result location
            with ex ->
                printfn "\tA:%s  %s" result ex.Message
    }

main () |> Async.AwaitTask |> Async.RunSynchronously
