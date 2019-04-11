var ws = require("nodejs-websocket");
var request = require("request");

var util = require("./modules/utils.js");
var appsetting = require("./modules/appsetting.js");

var createWebsocketConn = (function () {

    var conn_opts = {
        extraHeaders: [
            { X: "123456" },
            { Y: "456789" }
        ]
    };
    var conn_close = function (code, reason) {
        console.log(`conn close ${code} ${reason}`);
    };
    var conn_error = function (err) {
    };
    var conn_text = function (str) {
        console.log(str);
    };
    var conn_connect = function () {
        console.log('connect');
    };

    return function (url) {
        var conn = ws.connect(url, conn_opts);
        conn
            .on('connect', conn_connect)
            .on('text', conn_text)
            .on('error', conn_error)
            .on('close', conn_close);
    };

})();

//以下是测试逻辑
var imserver = appsetting.getItem("imserver");
if (util.isEmpty(imserver)) {
    throw 'imserver not configed';
}
else {
    if (!util.endWith('/'))
        imserver = imserver + '/';
}

var logintestdata = [
    { userid: 'cjm', pwd: '123456', clientFlag: 'wechat' }
    //, { userid: 'xys', pwd: '123456', clientFlag: Math.random() }
    //, { userid: 'lqy', pwd: '123456', clientFlag: Math.random() }
];
var token_list = [];
for (var index in logintestdata) {
    request.post(`${imserver}session/login`, { form: logintestdata[index] }, function (err, httpResponse, body) {
        if (httpResponse.statusCode !== 200) {
            console.log(`request url ${httpResponse.url} error`);
        } else {
            var re = JSON.parse(body);
            if (re.error) {
                console.log(`login error. ${re.error.message}`);
            } else {
                createWebsocketConn(`wss://localhost:31611/msghandler?accesstoken=${re.value.token}`);
                token_list.push(re.value.token);
                console.log(`${re.value.token} login ok `);
            }
        }
    });
}

//保持心跳
setInterval(function () {

    var cb = function (accesstoken) {
        return function (err, httpResponse, body) {
            if (httpResponse.statusCode !== 200) {
                console.log(`request url ${httpResponse.url} error`);
            } else {
                console.log(`token ${accesstoken} keep alive`);
            }
        };
    };

    for (var index in token_list) {
        request.post(
            `${imserver}session/keepalive`,
            { form: { accesstoken: token_list[index] } },
            cb(token_list[index]));
    }


}, 30 * 1000);
