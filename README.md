<div align="center">
  <img src="res/icon2.png" alt="RakNexus Logo" width="256" />
</div>

<h1 align="center">RakNexus</h1>
<p align="center">C# port of RakNet 3.92</p>

## Features  
- **Pure C# Implementation**: A complete rewrite of the RakNet protocol (v3.902 / Protocol 13) using modern .NET 8.
- **Reliable UDP Protocol**: Implements the full reliability layer including ACKs, NAKs, packet ordering, and sequencing.
- **Data Handling**: Includes a robust `RakBitStream` for efficient bit-level serialization and compression.
- **Advanced Networking**: Supports split packets, congestion control (UDT), and custom types like `uint24`.
- **Async Core**: Built with asynchronous patterns for high-performance socket handling suitable for game servers.

## How to Use  
1. Clone the repository and add the project to your solution.
2. Ensure you have the **.NET 8 SDK** installed.
3. Initialize the listener in your server application:
   ```csharp
   using RakNexus.Network;

   // Create a listener on a specific port
   var listener = new RakNetListener(10001);

   // Handle new connections
   listener.SessionConnected += (session) => 
   {
       Console.WriteLine($"New client connected: {session.Address}");
       
       // Handle incoming packets
       session.PacketReceived += (packet) => 
       {
           Console.WriteLine($"Received Packet ID: {packet.PacketId}");
       };
   };

   // Start the receive loop
   await listener.StartAsync();