const fs = require("fs");
const ws = require("nodejs-websocket");
const request = require("request");

const util = require("./modules/utils.js");
const appsetting = require("./modules/appsetting.js");
const redisutil = require("./modules/redisutil.js");

var imserver = appsetting.getItem("imserver");
if (util.isEmpty(imserver)) {
    throw 'imserver not configed';
}
else {
    if (!util.endWith('/'))
        imserver = imserver + '/';
}

//服务器的全局ID，在集群环境下会使用此ID
var serverId = 0;
var serverPort = 31611;
var enabledSSL = false;
if (process.argv.length > 2) {
    var args = process.argv.splice(2);
    if (args.length % 2 === 0) {
        for (var i = 0, len = args.length; i < len; i = i + 2) {
            var key = args[i];
            switch (key) {
                case '-sid':
                    serverId = parseInt(args[i + 1], 10);
                    break;
                case '-p':
                    serverPort = parseInt(args[i + 1], 10);
                    break;
                case '-ssl':
                    enabledSSL = args[i + 1] === 'true' || args[i + 1] === '1';
                    break;
            }
        }
    }
}

//输出日志
var outlog = function(s) {
    if (s && s.length > 0) {
        console.log(`[${serverId}] ${s}`);
    }
};

//对websocket connection的业务化包装
const clientPipe = (function () {

    //util
    var _getValFromUrlParam = function (v) {

        if (!v)
            return '';
        if (v.length === 0)
            return '';
        v = decodeURIComponent(v);
        var arr = v.split(',');
        if (arr.length <= 1)
            return v;
        else
            return arr;
    };
    var _sendMessage = function (conn, msg, pipecmd) {
        var t = typeof msg;
        if (pipecmd === true) {
            conn.sendText('pipecmd,' + msg);
        } else {
            if (t === 'string')
                conn.sendText('text,' + msg);
            else if (t === 'object')
                conn.sendText('json,' + JSON.stringify(msg));
        }
    };

    return function (conn, pipeid) {

        this.id = pipeid;
        this.conn = conn;
        this.path = conn.path;
        this.extdata = {};

        //初始化参数
        var urlQueryString = this.path.substr(this.path.indexOf('?') + 1);
        var arr = urlQueryString.split('&');
        if (arr && arr.length > 0) {
            for (var i = 0, len = arr.length; i < len; i++) {
                var arr2 = arr[i].split('=');
                var pname = arr2[0];
                if (pname && pname.length > 0) {
                    if (pname.indexOf('extdata_') === 0) {
                        this.extdata[pname.replace('extdata_', '')] = arr2.length === 2 ? _getValFromUrlParam(arr2[1]) : '';
                    } else {
                        this[pname] = arr2.length === 2 ? _getValFromUrlParam(arr2[1]) : '';
                    }
                }
            }
        }

        this.sendMessage = function (msg) {
            if (this.conn) {
                if (this.conn.readyState === 0) {//CONNECTING
                    var count = 0;
                    while (this.conn.readyState === 0) {

                        if (count >= 100) {
                            break;
                        }
                        if (this.conn.readyState === 1) {
                            _sendMessage(this.conn, msg);
                            break;
                        }
                        count++;
                    }

                } else if (this.conn.readyState === 1) {//OPEN
                    _sendMessage(this.conn, msg);

                } else if (this.conn.readyState === 2) {//CLOSING
                } else if (this.conn.readyState === 3) {//CLOSED
                }
            }
        };

    };

})();

//对websocket server的业务化包装
const createPipeManager = function (server, servercfg) {

    var _pipe_cache = {};
    var _server = server;
    var _servercfg = servercfg;
    var _createPipe = function (conn, pipeid) {
        var pipe;
        try {
            pipe = new clientPipe(conn, pipeid);
        } catch (e) {
            outlog(e);
            conn.close(5002, e);
            pipe = undefined;
        }
        if (pipe) {
            _pipe_cache[pipeid] = pipe;
        }
        return pipe;
    };
    var _getValidateSignCallback = function (conn, token_validate_url, success) {
        return function (error, response, body) {
            if (response.statusCode === 200) {
                var re = JSON.parse(body);
                if (re.error) {
                    conn.close(5001, re.error.message);
                } else {
                    success(conn, re.value);
                }
            }
            else {
                outlog(`request url error -> ${token_validate_url}`);
            }

        };

    };
    var _conn_callback = function (pipe) {
        return {
            close: function (code, reason) {

                outlog(`pipe ${pipe.id} closed -> ${code} ${reason}`);
                delete _pipe_cache[pipe.id];

                //更新客户端最后活动时间
                _redis_updateUserLastActiveTime(pipe.id, 0);

                //向集群管理器上报本服务器的连接个数
                _redis_setConnectionCount(_server.connections.length);

            },
            error: function (err) {
                //outlog(`pipe ${pipe.id} err -> ${err}`);

                //向集群管理器上报本服务器的连接个数
                _redis_setConnectionCount(_server.connections.length);

            },
            text: function (str) {
                //outlog(`pipe ${pipe.id} received text -> ${str}`);

                //处理收到的心跳包数据
                if (str.length > 3) {
                    if (str.substr(0, 3) === 'hb_') {
                        pipe.sendMessage(str);
                        //更新客户端最后活动时间
                        _redis_updateUserLastActiveTime(pipe.id, util.toUnixTime(new Date()));
                    }
                }
            },
            binary: function (inStream) {
                outlog(`pipe ${pipe.id} received bin data`);
            },
            connect: function () {
                outlog(`pipe ${pipe.id} connect`);
            },
            pong: function (data) {
                outlog(`pipe ${pipe.id} pong -> ${data}`);
            }
        };
    };
    var _update_ClientCount = function (server) {
        return function () {
            if (server) {
                _redis_setConnectionCount(server.connections.length);
                setTimeout(_update_ClientCount(_server), 30 * 1000);
            }
        };
    };
    var _server_callback = {
        close: function () {

            outlog('server closed');

            //从集群管理器中移除
            _redis_setConnectionCount(0, 'remove');

        },
        error: function (errObj) {

            outlog('server error (' + JSON.stringify(errObj) + ')');

            //从集群管理器中移除
            _redis_setConnectionCount(0, 'remove');

        },
        listen: function () {

            outlog(`server ${serverId} start listening ${_servercfg.port}`);
            if (enabledSSL) {
                outlog(`server ${serverId} SSL Enabled`);
            }

            //将自己放入集群管理器
            _redis_setConnectionCount(0);

            //定时向集群管理器上报本服务器的连接个数
            setTimeout(_update_ClientCount(_server), 30 * 1000);

        }
    };

    var _regex_match_accesstoken = /accesstoken=[^&]+/gi;
    var _regex_replace_accesstoken = /accesstoken=/gi;

    //客户端连接到达
    _server.on('connection', function (conn) {

        var match = conn.path.match(_regex_match_accesstoken);
        if (!match || match.length === 0) {
            conn.close(5000, 'reqired param -> accesstoken');
        }
        else {

            var accesstoken = match[0].replace(_regex_replace_accesstoken, util.emptyString);
            var validateUrl = `${imserver}session/getUserId?accesstoken=${accesstoken}`;
            request(validateUrl, _getValidateSignCallback(conn, validateUrl, function (conn, userid) {
                var p = _createPipe(conn, userid);
                if (p) {

                    var cb = _conn_callback(p);

                    //连接被关闭时触发
                    conn.on('close', cb.close);
                    //连接发生错误是触发
                    conn.on('error', cb.error);
                    //收到文本数据时触发
                    conn.on('text', cb.text);
                    //收到二进制数据后触发
                    conn.on('binary', cb.binary);
                    //连接确定时可用
                    conn.on('connect', cb.connect);
                    //服务器被ping后触发
                    conn.on('pong', cb.pong);

                    outlog(`pipe ${userid} connected`);

                    //更新客户端最后活动时间
                    _redis_updateUserLastActiveTime(userid, util.toUnixTime(new Date()));

                    //向集群管理器上报本服务器的连接个数
                    _redis_setConnectionCount(_server.connections.length);

                }
                else {
                    conn.close(5003, 'create pipe failed');
                }

            }));
        }
    });

    //服务器被关闭
    _server.on('close', _server_callback.close);
    //服务器级错误
    _server.on('error', _server_callback.error);
    //开始监听
    _server.listen(_servercfg.port, _server_callback.listen);

    return {

        //管道相关
        getPipe: function (pipeId) {
            if (typeof _pipe_cache[pipeId] !== 'undefined') {
                var pipe = _pipe_cache[pipeId];
                if (typeof pipe['conn'] !== 'undefined') {
                    if (pipe.conn.readyState === 0 || pipe.conn.readyState === 1)
                        return pipe;
                    else {
                        delete _pipe_cache[pipeId];
                        return null;
                    }
                }
            }
        },
        getAllPipe: function () {
            var arr = [];
            for (var k in _pipe_cache) {
                arr.push(_pipe_cache[k]);
            }
            return arr;
        },

        //获取服务器相关
        getServer: function () {
            return _server;
        },
        getAllConn: function () {
            if (_server && _server.connections)
                return _server.connections;
            else
                return [];
        },
        getConnCount: function () {
            if (_server && _server.connections)
                return _server.connections.length;
            else
                return 0;
        }

    };

};

//创建管道管理器，以便接收客户端的连接请求
var serverConfig = { secure: enabledSSL };
if (serverConfig.secure) {
    serverConfig.key = fs.readFileSync('2_handday.com.key');
    serverConfig.cert = fs.readFileSync('1_handday.com_bundle.crt');
}
const clientPipeManager = createPipeManager(ws.createServer(serverConfig), { port: serverPort || 31611 });

//订阅后端redis的新消息发布通道
const CHANNEL_NAME = 'newmsg_toclient';
const REDIS_USER_KEY = "REDIS_USER_KEY";
const REDIS_SESSION_KEY = "REDIS_SESSION_KEY";
const REDIS_STATE_KEY = "REDIS_STATE_KEY";
const REDIS_MSGCACHE_KEY = "REDIS_MSGCACHE_KEY";
const REDIS_MSGPOST_KEY = "REDIS_MSGPOST_KEY";
const REDIS_MSGMQ_KEY = "REDIS_MSGMQ_KEY";
const REDIS_TOCLIENT_CMD = "areyouok";

//记录通道最后一次收到心跳消息的时间
var _channel_lastactivetime = new Date();

var _redis_setConnectionCount = function (count, mode) {

    if (!mode || mode.length === 0) {
        mode = 'update';
    }

    if (mode === 'update') {
        redisutil.getClient(REDIS_STATE_KEY, function (error, client) {
            if (error)
                outlog(`connect to REDIS_STATE error. errmsg:${error}`);
            else {
                client.hset('websocketserver_clientcount', serverId, `${count},${util.toUnixTime(new Date())}`);
            }
            client.quit();
        });
    }
    else if (mode === 'remove') {
        redisutil.getClient(REDIS_STATE_KEY, function (error, client) {
            if (error)
                outlog(`connect to REDIS_STATE error. errmsg:${error}`);
            else {
                client.hdel('websocketserver_clientcount', serverId);
            }
            client.quit();
        });
    }

};
var _redis_updateUserLastActiveTime = function (userid, time) {
    redisutil.getClient(REDIS_SESSION_KEY, function (error, client) {
        if (error)
            outlog(`connect to REDIS_SESSION error. errmsg:${error}`);
        else {
            client.set(`imuser_lastactivetime_${userid}`, time);
        }
        client.quit();

    });
};
var _redis_channelCallback = function (channel, post_task) {
    if (channel === CHANNEL_NAME) {
        if (!util.isEmpty(post_task)) {

            //来自进程管理器发送的通道探活消息
            if (post_task === REDIS_TOCLIENT_CMD) {
                _channel_lastactivetime = new Date();
            } else {

                //新的投送任务到达

                outlog(`post message task: ${post_task}`);

                var data = post_task.split(',');
                var datalen = data.length;
                if (datalen >= 2) {
                    _redis_getMessage(data[0]/*msg_uid*/, function (msg) {
                        for (var i = 1; i < datalen; i++) {
                            var pipe = clientPipeManager.getPipe(data[i]/*userid*/);
                            if (!pipe) {
                                //outlog(`post message error. msg_uid:${msg.msgid}. user pipe not found.${data[i]}`);
                            } else {
                                msg.receiver = pipe.id;
                                pipe.sendMessage(msg);
                                outlog(`post message ${msg.msgid} to ${pipe.id} client`);
                            }
                        }
                    });
                }

            }

        }
    }
};
var _redis_getMessage = function (msg_uid, cb) {

    redisutil.getClient(REDIS_MSGCACHE_KEY, function (error, client) {
        if (error) {
            outlog(`post message error. msg_uid:${msg_uid}. errmsg:${err}`);
        }
        else {
            client.hgetall(`msg_${msg_uid}`, function (err, obj) {
                if (error)
                    outlog(`post message error. msg_uid:${msg_uid}. errmsg:${err}`);
                else {
                    cb({
                        msgid: obj['msgid'],
                        msgtype: parseInt(obj['msgtype']),
                        sender_usertype: parseInt(obj['sender_usertype']),
                        sender_userid: obj['sender_userid'],
                        body: util.isEmpty(obj['body']) ? null : JSON.parse(obj['body']),
                        sendtime: util.isEmpty(obj['sendtime']) ? 0 : parseFloat(obj['sendtime']),
                        extinfo: obj['extinfo']
                    });
                }
            });
        }
        client.quit();

    });

};

//定时测试订阅通道
var _redis_channelHeartbeatTest_interval = 10 * 1000;
var _redis_channelHeartbeatTest = function () {
    if ((new Date() - _channel_lastactivetime) >= 60 * 1000) {
        //退出进程
        process.exit(10);
    } else {
        //继续检测通道状态
        setTimeout(_redis_channelHeartbeatTest, _redis_channelHeartbeatTest_interval);
    }
};
var _redis_connectionPostServiceChannel = function () {

    //建立和后端redis的订阅连接，以便接收来自postservices的投递通知
    //如果这里和服务器的连接不可靠可能导致消息无法即时投递，需要确保这里的连接是可靠的
    redisutil.getClient(REDIS_MSGMQ_KEY, function (error, client) {
        if (error)
            outlog(`connect to REDIS_MSGMQ error. errmsg:${error}`);
        else {
            client.on("message", _redis_channelCallback);
            client.subscribe(CHANNEL_NAME);
            //启动定时的心跳探测
            setTimeout(_redis_channelHeartbeatTest, _redis_channelHeartbeatTest_interval);
        }
    });

};
_redis_connectionPostServiceChannel();