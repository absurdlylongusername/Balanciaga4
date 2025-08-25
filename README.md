# Balanciaga4 - TCP Layer 4 Load Balancer

Bonjour Bonjour! Here is my solution to the Payroc Technical Test.

This was a very interesting problem that seemed simple enough at first, but I quickly discovered it had a lot of nuance and was a lot more difficult than I expected, on top of seemingly esoteric bugs that took a while to fix.

## Ideal Solution

My ideal solution was to have a TCP/UDP layer 4 load balancer. 

It would have hot swappable load balancing policies: 
- round robin
- least connections (pick route to the server with least number of connections)
- weighted round robin
- Source IP hash (sticky routing) - ensure client mapped to same endpoint on each connection
- Some others

As well as being able to hot swap with servers exist in the balancing pool.

It would also have health checks to check healthy servers and remove them from the pool when they go offline, and add them back when they come back online.

Intelligent metrics and logging: E.g. active connections, throughput per second or minute, balancing distribution, etc.

## My solution

TCP layer 4 load balancer with round robin policy and health checks, with some logging.

I also wrote some integration tests to help me confirm that it actually works, to avoid the scenario where I spend ages writing code only to find out it doesn't work lol.

### General code flow and architecture

#### Configuration

LbOptions stores the config, which gets read in from appsettings.json, command line args, environment variables, etc. The normal precedence for [configuration in .NET](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0).

Configuration is bound and validated in `LbOptionsConfigurator` and `LbOptionsValidator`.

The code uses an `IOptionsMonitor` which is supposed to hot reload the options whenever they change. I never actually got to test if this works though.

#### TCP Listener

`TcpListenerService` is what listens on the endpoint passed in from config. If the listening endpoint is loopback or `IPAddress.Any` then it will listen in dual mode in IPV6.



New connections dispatch a task to handle load balancing for that client in `ConnectionDispatcher.DispatchAsync`. The current `LoadBalancingPolicy` will be used to select which server to route the client to.

#### ProxySession

`ProxySession` is what handles the main routing logic.

It opens TCP connection from LB to the selected backend server, and the begins data transfer between client and LB, and the LB and the server. 

If it takes too long to connect to the backend server, or the client or server become idle for too long and stop sending data, it will cancel the whole operation.

#### StreamBytePump

Simple implementation that reads from the source stream into a buffer, then writes to the destination stream. Uses an Array pool for the byte buffer for better performance to prevent allocating many large buffers in memory which would trigger the GC a lot.

#### NotifyingStream

Wrapper around a stream used a decorator to make it easier to log when bytes are written on read/write, instead of putting the logic in StreamBytePump. 

#### BackendRegistry

Singleton service for managing pool of backend servers.
Handles the logic for adding servers to the pool, getting info about a server endpoint, marking endpoints as up or down, and updating the pool when the configuration is updated.

#### TcpHealthCehecker

Periodically pings each endpoint on a timer to check if it's up. Uses the config to determine conditions for being up or down. If an endpoint is pinged successfully X number of times, it's considered UP, if it fails Y number of times, it's considered down.

Uses a random jitter period to introduce randomness to pinging endpoints to avoid situations where endpoint may falsely be marked as down due to a certain slowdown/hanging that may happen on a fixed, predictable interval.

* * *

### Notable bugs encountered

#### Manual testing

After writing a significant amount of code, it occurred to me I should probably test it to make sure it actually works.

I initially did this by just running it, and then started two python servers in the Testing directory (in 2 different terminals), and then spamming the load balancer endpoint with `curl` and `bombardier` (from another terminal)


```ps
python -m http.server 9001 --directory .\Alpha\
python -m http.server 9002 --directory .\Beta\

# other terminal

curl http://localhost:8080
bombardier.exe -c 200 -d 3s http://localhost:8080
```

This seemed to work fine, so then I wanted to make some integration tests to automate this a little bit.

#### Integration tests

**HttpClient tests** \
I started with some initial integration tests to just test that round robin works as intended.

The test seemed simple enough to make, but it wasn't functioning as intended: I expected to see responses from both backends but only saw from one of them.

After much debugging, logging and research, I discovered the issue was because the default HttpClient does not close the TCP Connection on each request (which makes sense for performance), so each request would use the connection and get routed to the same server each time.

After doing some tweaks to force the connection to be closed, the test passed as expected.

**IPv6 vs IPv4** \
After getting the round robin tests to pass, I saw it was taking 8 seconds to complete, whereas the manual tests could handle messages within milliseconds.

After adding some improved logging in key places, I discovered this was again due to test setup and not the server (although it was kind of both).

The issue was, the HttpClient defaults to trying IPv6 for localhost, and then tries IPv4, with a timeout of 2s. Since my TcpListenerService was only listening for IPv4, it would add 2s timeout for every request in the test.

This was easily confirmed by changing the endpoint in the config from `127.0.0.1` to `::1`.

This resulted in me changing the code to ensure that the load balancer listens in dual mode if the endpoint is localhost.

**Non-determinism of routing** \
After adding health checks, I added logic in BackendRegistry that stores the endpoints in a `ConcurrentDictionary`. However, my solution didn't ensure that endpoints are returned in the order that are put into the dictionary (I was doing `Dictionary.Keys.ToList()`).

This resulted in tests failing because the wrong endpoint was being hit. After adding OrderedEndpoints to ensure endpoints are returned in the order they exist in the config, the tests passed again.