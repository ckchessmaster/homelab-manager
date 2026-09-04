const net = require('net');

function createForwarder(listenPort, targetPort) {
  const server = net.createServer((clientSocket) => {
    const targetSocket = net.connect(targetPort, '127.0.0.1');
    clientSocket.pipe(targetSocket);
    targetSocket.pipe(clientSocket);
    clientSocket.on('error', () => targetSocket.destroy());
    targetSocket.on('error', () => clientSocket.destroy());
  });

  server.on('error', (err) => {
    console.error(`Error on port ${listenPort}:`, err.message);
  });

  server.listen(listenPort, '0.0.0.0', () => {
    console.log(`Proxy listening on 0.0.0.0:${listenPort} -> 127.0.0.1:${targetPort}`);
  });
}

createForwarder(5000, 5029);
createForwarder(5029, 5029);
