const spawn = require('child_process').spawn;
const redisutil = require("./modules/redisutil.js");

var exec = function (serverid) {

    var ssl = 1;
    //if (serverid === 5)
    //    ssl = 0;

    var node = spawn('node', ['server', '-sid', serverid, '-p', 31611 + serverid, '-ssl', ssl], { pwd: process.cwd() });
    node.stdout.on('data', processcallback(serverid, 'data'));
    node.stderr.on('data', processcallback(serverid, 'data'));
    node.on('close', processcallback(serverid, 'close'));

    return node;

};
var processcallback = function(serverid, event) {
    if (event === 'data')
        return function(data) { console.log(data.toString()); };
    else if (event === 'close')
        return function(code) {
            console.log(`server ${serverid} exitcode: ${code}`);
            console.log(`server ${serverid} is starting`);
            setTimeout(function() { exec(serverid); }, 3000);
        };
};

//启动5个进程来处理数据
for (var i = 0; i < 5; i++) {
    exec(i);
}

const CHANNEL_NAME = 'newmsg_toclient';
const REDIS_MSGMQ_KEY = "REDIS_MSGMQ_KEY";
const REDIS_TOCLIENT_CMD = "areyouok";

//启动定时器来发送订阅器探活消息
var notifychannel_keepalive = function () {
    redisutil.getClient(REDIS_MSGMQ_KEY, function (error, client) {
        if (error)
            outlog(`connect to REDIS_MSGMQ error. errmsg:${error}`);
        else {
            client.publish(CHANNEL_NAME, REDIS_TOCLIENT_CMD);
            setTimeout(notifychannel_keepalive, 5000);
        }
        client.quit();
    });
};
notifychannel_keepalive();