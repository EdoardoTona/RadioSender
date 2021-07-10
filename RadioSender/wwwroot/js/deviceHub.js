"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/deviceHub").build();

var container = document.getElementById("mynetwork");

var nodes = new vis.DataSet(nodes);
var edges = new vis.DataSet(edges);

var network = new vis.Network(
  document.getElementById("mynetwork"),
  { nodes, edges }, {
  interaction: {
    dragNodes: false,
    dragView: false,
  },
  layout: { randomSeed: 2 },
  nodes: {
    shape: "circle",
    scaling: {
      label: false,
      min: 1,
      max: 10
    }
  },
  edges: {
    scaling: {
      label: false,
      min: 1,
      max: 10
    }
  }
});
//Disable send button until connection is established
//document.getElementById("sendButton").disabled = true;

connection.on("graph", function (e, n) {
  console.log(e, n);

  network.setData({
    nodes: new vis.DataSet(n),
    edges: new vis.DataSet(e)
  })
  //network.
  //nodes.clear();
  //edges.clear();
  //nodes.add(n);
  //edges.add(e);

});

connection.start();

connection.on("disconnected", function () {
  console.log("disconnected");
  setTimeout(function () {
    connection.start();
  }, 5000); // Restart connection after 5 seconds.
});

//connection.start().then(function () {
//  document.getElementById("sendButton").disabled = false;
//}).catch(function (err) {
//  return console.error(err.toString());
//});

//document.getElementById("sendButton").addEventListener("click", function (event) {
//  var user = document.getElementById("userInput").value;
//  var message = document.getElementById("messageInput").value;
//  connection.invoke("SendMessage", user, message).catch(function (err) {
//    return console.error(err.toString());
//  });
//  event.preventDefault();
//});