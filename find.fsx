#r "nuget: Ae.Dns.Client, 3.0.0"

open System
open System.Net.Http
open Ae.Dns.Client
open Ae.Dns.Protocol
open System.Diagnostics

let availableDnsServers =
    [
      // category: China DNS
      //   "https://dns.alidns.com/dns-query"
      //   "https://223.5.5.5/dns-query"
      //   "https://223.6.6.6/dns-query"
      //   "https://doh.pub/dns-query"
      //   "https://1.12.12.12/dns-query"
      //   "https://120.53.53.53/dns-query"
      //   "https://doh.360.cn/dns-query"
      //   "https://doh.apad.pro/dns-query"
      // category: Public DNS
      "https://dns.google/dns-query"
      "https://cloudflare-dns.com/dns-query"
      "https://jp.tiarap.org/dns-query"
      "https://dns.quad9.net/dns-query"
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
        use httpClient = new HttpClient()
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

let main () =
    task {
        let availablensServers = System.Collections.Generic.List<string * int64>()

        for dnsServer in availableDnsServers do
            try
                let address = "www.google.com"
                let! result, times = queryEvaluate address dnsServer
                printfn "[+] Average Time: %d ms | %s | Response: %A" times dnsServer result.Answers[0].Resource
                availablensServers.Add(dnsServer, times)
            with ex ->
                printfn "[ ] %s | Some error happened: %s" dnsServer ex.Message
        printfn "Sorted DNS Servers:"
        availablensServers
        |> Seq.sortBy snd
        |> Seq.iter (fun (dnsServer, times) -> printfn "%d ms \t %s" times dnsServer)
    }

main () |> Async.AwaitTask |> Async.RunSynchronously
