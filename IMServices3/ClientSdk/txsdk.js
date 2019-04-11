var MINI_PROGRAM = false;
if (typeof wx !== 'undefined') {
    if (typeof wx.request === 'function') {
        MINI_PROGRAM = true;
    }
}

var zztx_tx = (function () {

    var MsgSendState = { sending: 0, success: 1, failed: 2 };
    var UserType = { employee: 1, system: 2, app: 3, openid: 4 };
    var MsgType = { chat_text: 1, chat_pictures: 2, chat_pos: 3, chat_file: 4, chat_welcome: 5, system_app: 60, system_push: 70, system_cmd: 80 };
    var ClientFlag = { CLIENT_FLAG_SERVER: 'system', CLIENT_FLAG_WECHAT: 'wechat', CLIENT_FLAG_MINIPROGRAM: 'miniprogram' };

    //发送心跳的间隔
    var sendHeartbeat_interval = 10;
    //检测心跳的间隔
    var checkHeartbeat_interval = 10;
    //出错后重新登录的间隔
    var relogin_interval = 10;

    var G_cfg = null;/*全局配置*/
    var G_currentSession = null;/*当前登录会话*/
    var G_currentWsConn = null;/*当前WS连接*/

    var outLog = function (msg) {
        console.log('[SDKLOG] ' + new Date() + ' ' + msg);
    };
    var post = (function () {

        var fail_ajax = function (xhr, errorType, error) {
            outLog("请求出错");
        };
        var fail_miniprogram = function () {
            outLog("请求出错");
        };
        var success = function (cb) {
            return function (res) {
                if (!MINI_PROGRAM)
                    cb(res);
                else
                    cb(res.data);
            };
        };

        return function (url, data, cb) {
            if (!MINI_PROGRAM) {
                $.ajax({
                    url: url,
                    data: data,
                    type: 'POST',
                    success: success(cb),
                    error: fail_ajax
                });
            }
            else {
                wx.request({
                    url: url,
                    data: data,
                    method: 'POST',
                    success: success(cb),
                    fail: fail_miniprogram
                });
            }
        };

    })();
    var WSAdptor = function (wsurl) {

        var ws = MINI_PROGRAM
            ? wx.connectSocket({ url: wsurl })
            : new WebSocket(wsurl);

        var emptyFn = function () { };
        var self = this;

        this.send = (function () {

            var mp_success = function () { };
            var mp_fail = function () { };

            return function (data) {
                if (ws) {
                    if (MINI_PROGRAM) {
                        ws.send({ data: data, success: mp_success, fail: mp_fail });
                    } else {
                        ws.send(data);
                    }
                }
            };

        })();
        this.close = function () {
            if (ws) {
                ws.close();
                ws = null;
            }
        };

        var user_event = {
            onopen: emptyFn,
            onclose: emptyFn,
            onmessage: emptyFn,
            onerror: emptyFn
        };
        var cb = {
            onopen: function (res) {
                user_event.onopen(res, self);
            },
            onclose: function (res) {
                user_event.onclose(res, self);
            },
            onmessage: function (res) {
                var data = res.data;
                if (data && data.length > 0) {
                    var index = data.indexOf(',');
                    var type = data.substring(0, index);
                    data = data.substr(index + 1);
                    if (type === 'json') {
                        data = JSON.parse(data);
                    }
                    user_event.onmessage(data, type, self);
                }
            },
            onerror: function (res) {
                if (MINI_PROGRAM) {
                    user_event.onerror(res.errMsg, self);
                } else {
                    user_event.onerror(res, self);
                }
            }
        };

        //事件
        this.onopen = function (onopen) {
            if (onopen && typeof onopen === 'function') {
                user_event.onopen = onopen;
            }
            if (MINI_PROGRAM) {
                ws.onOpen(cb.onopen);
            } else {
                ws.onopen = cb.onopen;
            }
        };
        this.onclose = function (onclose) {
            if (onclose && typeof onclose === 'function') {
                user_event.onclose = onclose;
            }
            if (MINI_PROGRAM) {
                ws.onClose(cb.onclose);
            }
            else {
                ws.onclose = cb.onclose;
            }
        };
        this.onmessage = function (onmessage) {
            if (onmessage && typeof onmessage === 'function') {
                user_event.onmessage = onmessage;
            }
            if (MINI_PROGRAM) {
                ws.onMessage(cb.onmessage);
            }
            else {
                ws.onmessage = cb.onmessage;
            }
        };
        this.onerror = function (onerror) {
            if (onerror && typeof onerror === 'function') {
                user_event.onerror = onerror;
            }
            if (MINI_PROGRAM) {
                ws.onError(cb.onerror);
            }
            else {
                ws.onerror = cb.onerror;
            }
        };

    };
    WSAdptor.isSupported = function () {
        if (MINI_PROGRAM) {
            //小程序是支持websocket的不用判断
            return true;
        } else {
            return typeof WebSocket !== 'undefined';
        }
    };

    //构建文字消息
    var buildTextMsg = function (text, receiverList, extinfo) {

        if (!receiverList || receiverList.length === 0) {
            throw '接收人不能为空';
        }
        if (!text || text.length === 0) {
            throw '文字消息内容不能为空';
        }

        return {
            msgtype: MsgType.chat_text,
            body: { text: text },
            receivers: receiverList.join(','),
            extinfo: extinfo
        };

    };

    //构建图片消息
    var buildPictrueMsg = function (pictureList, receiverList, extinfo) {

        if (!receiverList || receiverList.length === 0) {
            throw '接收人不能为空';
        }
        if (!pictureList) {
            throw '图片消息至少应该包含1张图片';
        }
        if (typeof pictureList.length !== 'number') {
            //非数组就包装成数组
            pictureList = [pictureList];
        }
        for (var index in pictureList) {
            var pic = pictureList[index];
            if (typeof pic.orgUrl === 'undefined' || pic.orgUrl.length === 0)
                throw '图片消息缺少必须的参数 orgUrl';
            if (typeof pic.thubUrl === 'undefined' || pic.thubUrl.length === 0)
                throw '图片消息缺少必须的参数 thubUrl';
        }

        return {
            msgtype: MsgType.chat_pictures,
            body: { pictures: pictureList },
            receivers: receiverList.join(','),
            extinfo: extinfo
        };

    };

    //将unix时间转为js日期时间
    var asDateFromUnixtime = function (unixtime) {
        return new Date(unixtime * 1000);
    };

    //判断是否登录
    var isLogin = function () {
        if (G_currentSession === null)
            return false;
        if (new Date() - asDateFromUnixtime(G_currentSession.expiry) > (2 * 60 - 10) * 60 * 1000) {
            return false;
        }
        return true;
    };

    //心跳管理器
    var connHeartbeatManager = (function () {

        var lastSendHeartbeat = null
            , checkHeartbeat_checktimer = null
            , sendHeartbeat_checktimer = null;

        var sendHeartbeat = function (wsa) {

            sendHeartbeat_checktimer = setTimeout(function () {

                lastSendHeartbeat = 'hb_' + new Date().getTime();
                wsa.send(lastSendHeartbeat);

                outLog(G_cfg.userid + ' Send Heartbeat ' + lastSendHeartbeat);

                //如果10s都不能返回就说明已经超时
                checkHeartbeat_checktimer = setTimeout(checkHeartbeat, checkHeartbeat_interval * 1000);

            }, sendHeartbeat_interval * 1000);

        };

        var checkHeartbeat = function () {
            if (lastSendHeartbeat !== null) {

                outLog(G_cfg.userid + ' Heartbeat timeout. ' + lastSendHeartbeat);

                //执行心跳超时的处理逻辑
                //长期未收到心跳数据说明服务器端或网络可能已经中断
                //需要尝试重新连接
                login();


            }
        };

        var processHeartbeat = function (wsa, data) {
            if (data === lastSendHeartbeat) {
                lastSendHeartbeat = null;//标记为已处理
                if (!checkHeartbeat_checktimer) {
                    clearTimeout(checkHeartbeat_checktimer);
                    checkHeartbeat_checktimer = null;
                }
                sendHeartbeat(wsa);
            }
        };
        var clearHeartbeatCheck = function () {
            if (sendHeartbeat_checktimer) {
                clearTimeout(sendHeartbeat_checktimer);
                sendHeartbeat_checktimer = null;
            }
        };

        return {
            sendHeartbeat: sendHeartbeat,
            processHeartbeat: processHeartbeat,
            clearHeartbeatCheck: clearHeartbeatCheck
        };

    })();

    //创建websocket连接，支持的才链接，不支持的不链接
    var createWebSocketConn = (function () {

        var onOpen = function (evt, wsa) {
            outLog(G_cfg.userid + ' WebSocket onOpen');
            connHeartbeatManager.sendHeartbeat(wsa);
        };
        var onClose = function (evt, wsa) {

            outLog(G_cfg.userid + ' WebSocket onClose');

            //清除心跳包发送器
            connHeartbeatManager.clearHeartbeatCheck();

            //10秒后重新建立连接
            setTimeout(function () {
                login();
            }, relogin_interval * 1000);

        };
        var onMessage = function (data, type, wsa) {

            outLog(G_cfg.userid + ' WebSocket onMessage');

            if (type === 'text') {
                //处理心跳包
                if (data.length > 3) {
                    if (data.substring(0, 3) === 'hb_') {
                        connHeartbeatManager.processHeartbeat(wsa, data);
                    }
                }
            } else if (type === 'pipecmd') {
                //pipecmd
            } else if (type === 'json') {
                if (data && typeof G_cfg.im_pushMsgCallback === 'function') {
                    G_cfg.im_pushMsgCallback(data);
                }
            } else {
                outLog('不支持的通道数据类型');
            }
        };
        var onError = function (evt, wsa) {
            outLog(G_cfg.userid + ' WebSocket onError');
        };

        return function (token) {

            if (!WSAdptor.isSupported()) {
                outLog('WebSocket不支持');
                return;
            }

            if (G_currentWsConn !== null) {
                G_currentWsConn.close();
                G_currentWsConn = null;
            }

            //动态获取websocket服务器地址
            var url = G_cfg.im_apiServer + '/client/getWebSocketServer';
            post(url, { ssl: MINI_PROGRAM }, function (re) {
                if (re.error) {
                    outLog('获取WebSocketServer失败。（' + re.error.message + '）');
                } else {
                    var wsa = new WSAdptor(re.value + '?accesstoken=' + token);
                    wsa.onopen(onOpen);
                    wsa.onclose(onClose);
                    wsa.onmessage(onMessage);
                    wsa.onerror(onError);
                    G_currentWsConn = wsa;
                }
            });

        };

    })();

    var init = function (cfg) {

        if (!cfg) {
            throw 'cfg is null';
        }
        if (typeof cfg.im_apiServer === 'undefined' || cfg.im_apiServer.length === 0) {
            throw 'cfg item im_apiServer is empty';
        }
        if (typeof cfg.userid === 'undefined' || cfg.userid.length === 0) {
            throw 'cfg item userid is empty';
        }
        if (typeof cfg.userpwd === 'undefined' || cfg.userpwd.length === 0) {
            throw 'cfg item userpwd is empty';
        }

        //修复URL最后的斜杠
        if (cfg.im_apiServer.substr(cfg.im_apiServer.length - 1) === '/')
            cfg.im_apiServer = cfg.im_apiServer.substring(0, cfg.im_apiServer.length - 1);

        //保存为全局配置
        G_cfg = cfg;

        //暴露开放接口
        interfaceObj.msgType = MsgType;
        interfaceObj.userType = UserType;
        interfaceObj.msgSendState = MsgSendState;
        interfaceObj.getLastestMessage = getLastestMessage;
        interfaceObj.getNotReadList = getNotReadList;
        interfaceObj.getChatNotReadCount = getChatNotReadCount;
        interfaceObj.getNotReadCount = getNotReadCount;
        interfaceObj.updateReadState = updateReadState;
        interfaceObj.sendMsg = sendMsg;
        interfaceObj.login = login;
        interfaceObj.buildTextMsg = buildTextMsg;
        interfaceObj.buildPictrueMsg = buildPictrueMsg;
        interfaceObj.asDateFromUnixtime = asDateFromUnixtime;

    };

    //非小程序增加一个事件
    //监听页面是否进入了后台
    var initUiStateListener = function () {

        var updateClientUiState = function (state) {
            var url = G_cfg.im_apiServer + '/client/updateClientUiState';
            var p = { userid: G_cfg.userid, state: state };
            post(url, p, function (re) {
                if (re.error) {
                    outLog('updateClientUiState error. ' + re.error.message);
                }
            });
        };

        return function () {
            if (!MINI_PROGRAM) {
                $(document).on("visibilitychange", function () {
                    updateClientUiState(document.visibilityState);
                    //debug
                    //if (document.visibilityState === "visible") {
                    //console.log(`${new Date()} visible`);
                    //}
                    //else if (document.visibilityState === "hidden") {
                    //console.log(`${new Date()} hidden`);
                    //}
                });
                updateClientUiState('visible');
            }
        };

    }();

    //登录
    var login = function (cb) {

        var url = G_cfg.im_apiServer + '/session/login';
        var p = {
            userid: G_cfg.userid,
            pwd: G_cfg.userpwd,
            clientFlag: ClientFlag.CLIENT_FLAG_WECHAT
        };

        post(url, p, function (result) {

            if (result.error) {

                outLog('登录失败。' + result.error.message);

                if (cb && typeof cb === 'function')
                    cb(result);


            } else {

                outLog('登录成功。' + G_cfg.userid);

                G_currentSession = result.value;

                //创建websocket链接
                createWebSocketConn(G_currentSession.token);

                //初始化页面UI状态监视器
                initUiStateListener();

                //调用用户回调
                if (cb && typeof cb === 'function')
                    cb(G_currentSession);

            }

        });

    };

    //发消息
    var sendMsg = function () {

        var fn = function (msg, cb) {

            var url = G_cfg.im_apiServer + '/message/send';
            var p = {
                accesstoken: G_currentSession.token,
                msgtype: msg.msgtype,
                msgbody: JSON.stringify(msg.body),//这里必须以文本方式传过去
                receivers: msg.receivers
            };
            if (msg.extinfo && msg.extinfo.length > 0) {
                p.extinfo = msg.extinfo;
            }

            post(url, p, function (re) {

                if (re.error) {
                    outLog('SendMsg error. ' + re.error.message);
                    if (cb && typeof cb === 'function')
                        cb(re);
                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });

        };

        return function (msg, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(msg, cb);
                });
            } else {
                fn(msg, cb);
            }
        };

    }();

    //拉取最新历史消息
    var getLastestMessage = function () {
        var fn = function (chatuserid, pindex, count, cb) {
            var url = G_cfg.im_apiServer + '/message/getchatlist';
            var p = {
                accesstoken: G_currentSession.token,
                chatuserid: chatuserid,
                pageIndex: pindex || 1,
                pageSize: count
            };

            post(url, p, function (re) {

                if (re.error) {

                    outLog('GetChatList error. ' + re.error.message);

                    if (cb && typeof cb === 'function')
                        cb(re);

                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });
        };

        return function (chatuserid, pindex, count, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(chatuserid, pindex, count, cb);
                });
            } else {
                fn(chatuserid, pindex, count, cb);
            }
        };

    }();

    //拉取历史消息
    var getNotReadList = function () {

        var fn = function (msgtypes, cb) {
            var url = G_cfg.im_apiServer + '/message/getNotReadList';
            var p = {
                accesstoken: G_currentSession.token,
                msgtypes: msgtypes
            };

            post(url, p, function (re) {

                if (re.error) {

                    outLog('GetNotReadList error. ' + re.error.message);

                    if (cb && typeof cb === 'function')
                        cb(re);

                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });
        };
        return function (msgtypes, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(msgtypes, cb);
                });
            } else {
                fn(msgtypes, cb);
            }
        };
    }();

    //未读消息总数
    var getNotReadCount = function () {

        var fn = function (msgtypes, cb) {
            var url = G_cfg.im_apiServer + '/message/getNotReadCount';
            var p = {
                accesstoken: G_currentSession.token,
                msgtypes: msgtypes
            };

            post(url, p, function (re) {

                if (re.error) {

                    outLog('GetNotReadCount error. ' + re.error.message);

                    if (cb && typeof cb === 'function')
                        cb(re);

                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });
        };
        return function (msgtypes, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(msgtypes, cb);
                });
            } else {
                fn(msgtypes, cb);
            }
        };
    }();

    //对话未读消息总数
    var getChatNotReadCount = function () {

        var fn = function (ids, cb) {
            var url = G_cfg.im_apiServer + '/message/getChatNotReadCount';
            var p = {
                accesstoken: G_currentSession.token,
                chatuserids: ids
            };

            post(url, p, function (re) {

                if (re.error) {

                    outLog('GetChatNotReadCount error. ' + re.error.message);

                    if (cb && typeof cb === 'function')
                        cb(re);

                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });
        };
        return function (ids, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(ids, cb);
                });
            } else {
                fn(ids, cb);
            }
        };
    }();

    //更新阅读状态
    var updateReadState = function () {

        var fn = function (ids, cb) {
            var url = G_cfg.im_apiServer + '/message/updateReadState';
            var p = {
                accesstoken: G_currentSession.token,
                msg_uids: ids
            };

            post(url, p, function (re) {

                if (re.error) {

                    outLog('UpdateReadState error. ' + re.error.message);

                    if (cb && typeof cb === 'function')
                        cb(re);

                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });
        };
        return function (ids, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(ids, cb);
                });
            } else {
                fn(ids, cb);
            }
        };
    }();

    //获取消息阅读状态
    var getReadState = function () {

        var fn = function (msg_uids, cb) {
            var url = G_cfg.im_apiServer + '/message/getReadState';
            var p = {
                accesstoken: G_currentSession.token,
                msg_uids: msg_uids
            };

            post(url, p, function (re) {

                if (re.error) {

                    outLog('GetReadState error. ' + re.error.message);

                    if (cb && typeof cb === 'function')
                        cb(re);

                } else {
                    if (cb && typeof cb === 'function')
                        cb(re);
                }

            });
        };
        return function (msg_uids, cb) {
            if (!isLogin()) {
                login(function () {
                    fn(msg_uids, cb);
                });
            } else {
                fn(msg_uids, cb);
            }
        };
    }();

    var interfaceObj = { init: init };

    return interfaceObj;

})();

if (MINI_PROGRAM) {
    module.exports = zztx_tx;
}
