<div align="center">
  <img src="res/icon2.png" alt="RakNexus Logo" width="256" />
</div>

<h1 align="center">RakNexus</h1>
<p align="center">C# implementation of the RakNet 3.902 transport layer</p>

RakNexus is a from-scratch C# reimplementation of the RakNet 3.902 (protocol
version 13) UDP transport, written for [ReCap](https://github.com/JeanxPereira),
a Darkspore private server. It targets wire compatibility with the retail
Darkspore client rather than feature parity with the original RakNet library, so
it ports the subset of RakNet that the client actually exercises.

The wire format has been checked byte-for-byte against a packet capture of the
retail client connecting to the reference C++ server (dalkon's `darkspore_server`,
which links the original RakNet 3.902).

## Scope

RakNexus is server-side. It listens for incoming clients, runs the reliability
layer, and hands decoded packets to the host application. It does not implement
the parts of RakNet a server never needs (outbound connection initiation, the
plugin system, autopatcher, RPC, and similar).

Implemented:

- Offline connection handshake (open-connection request/reply, connection
  request, connection-request-accepted, new-incoming-connection, ping/pong).
- Reliability layer: ACK/NAK ranges, reliable, ordered and sequenced delivery,
  duplicate detection, and split-packet reassembly.
- `RakBitStream` bit-level serialization, RakNet integer compression, and the
  `uint24` type used for datagram and message numbers.
- Congestion control and QoS probe responses.

## Wire format notes

RakNet mixes endianness, and getting it wrong is the usual source of silent
interop failures, so the rules are spelled out here:

- Templated integer writes (`Write<T>`) are big-endian on the wire. This covers
  `ushort`, `uint`, `ulong`, `float`, `RakNetGUID`, and the `SystemAddress` port.
- `RakNetTime` is 8 bytes, big-endian. The reference protocol is built with
  `__GET_TIME_64BIT`, so connection timestamps and ping/pong times are 64-bit
  (a `CONNECTION_REQUEST_ACCEPTED` is 85 bytes, not 77).
- Datagram numbers and ACK/NAK range indices use `uint24`, little-endian and
  byte-aligned.
- `SystemAddress` writes the binary address bitwise-inverted in network order,
  followed by the port in big-endian.
- `WriteCompressed` strips zero bytes from the big-endian representation of the
  value.

## Building

Requires the .NET 9 SDK.

```
dotnet build
dotnet test
```

The unit tests assert the wire format against the captured ground-truth bytes
and exercise the reliability layer (handshake sizes, endianness, compression,
split-packet reassembly, ordered delivery, duplicate handling).

## Usage

```csharp
using RakNexus.Network;

var listener = new RakNetListener(10001);

listener.SessionConnected += session =>
{
    Console.WriteLine($"New client connected: {session.Address}");

    session.PacketReceived += packet =>
    {
        Console.WriteLine($"Received Packet ID: {packet.PacketId}");
    };
};

await listener.StartAsync();
```
