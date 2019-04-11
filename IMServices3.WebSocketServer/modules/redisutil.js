var redis = require('redis');

var __appsetting = require('./appsetting.js');
var __redisutil = { connCount: 0 };

var __do = function (err, redisclient, callback) {

    if (err) {
        __closeclient(redisclient);
        console.log(err);
    }
    if (callback && typeof callback === 'function') {
        var cli = null;
        if (!err) {
            cli = new __redisClient(redisclient);
            __redisClientPools[cli.Id] = cli;
            __redisutil.connCount++;
            console.log(`redisclient ${cli.Id} is connected. client totalcount is ${__redisutil.connCount} at in process`);
        }
        callback(err, cli);
    }
};
var __closeclient = function (redisclient) {
    if (redisclient && typeof redisclient['quit'] === 'function') {
        try {
            redisclient.quit();
        }
        catch (e) {
            console.log(e);
        }
        __redisutil.connCount--;
        if (__redisutil.connCount < 0)
            __redisutil.connCount = 0;
    }
};
//======适配器维护线程 开始======
var __redisClientPools = {};
var __redisClientCloseChanged = false;
var __redisClientPools_timerhandler = function () {
    try {
        for (var key in __redisClientPools) {
            var cli = __redisClientPools[key];
            if (typeof cli["lastAccessTime"] !== 'undefined') {
                if (cli.autoRelease) {
                    //超过10s未调用或waitRelease为true就是无效的待释放的cli
                    if ((new Date() - cli.lastAccessTime) / 1000 >= 10 || cli.waitRelease) {
                        cli.close();
                        delete __redisClientPools[key];
                    }
                }
            }
        }
        if (__redisClientCloseChanged) {
            console.log(`redisclient totalcount is ${__redisutil.connCount} at in process`);
            __redisClientCloseChanged = false;
        }
    }
    catch (e) {
        console.log('function __redisClientPools_timerhandler execute exception. ' + e);
    }

    setTimeout(__redisClientPools_timerhandler, 5000);

};
setTimeout(__redisClientPools_timerhandler, 5000);
//======适配器维护线程 结束======

var __redisClient = function (redisclient) {

    //适配器ID
    this.Id = undefined;
    while (!this.Id || typeof __redisClientPools[this.Id] !== 'undefined') {
        this.Id = 'redisclient_' + Math.round(Math.random() * 10000000);
    }

    //最后访问适配器的时间
    this.lastAccessTime = new Date();

    //是否自动释放适配器
    this.autoRelease = true;

    //适配器是否正等待释放,autoRelease=true此参数生效
    this.waitRelease = false;

    //适配器是否已被释放,autoRelease=true此参数生效
    this.isReleased = false;

    //客户端连接
    this.client = redisclient;

    //关闭连接
    this.close = function () {

        this.isReleased = true;

        __closeclient(this.client);

        delete __redisClientPools[this.Id];
        __redisClientCloseChanged = true;

        console.log(`redisclient ${this.Id} is closed`);

    };

    //执行collection中的接口函数
    this.exec = function () {

        if (arguments.length === 0)
            return;

        var cmdname = undefined,
            params = [];

        for (const i in arguments) params.push(arguments[i]);

        cmdname = params[0];
        params = params.slice(1);

        if (typeof this.client[cmdname] === 'function') {

            try {
                this.lastAccessTime = new Date();
                return this.client[cmdname].apply(this.client, params);
            }
            catch (e) {
                this.close();
                console.log(e);
            }

        }

    };

};

__redisutil.act = function (configKey, callback) {

    //args validate
    if (!configKey || configKey.length === 0) {
        __do(new Error('configKey is empty'), undefined, callback);
        return;
    }
    if (!callback || typeof callback !== 'function') {
        __do(new Error('callback not is a function'), undefined, callback);
        return;
    }

    //redisconfig validate
    var redisconfig = __appsetting.getItem(configKey);
    if (!redisconfig) {
        __do(new Error(`redisconfig ${configKey} not exist`), undefined, callback);
        return;
    }

    if (typeof redisconfig === 'string') {
        __do(null, redis.createClient(redisconfig), callback);
    }
    else {

        if (!redisconfig.host) {
            __do(new Error(`redisconfig ${configKey} host error`), undefined, callback);
        }
        else {

            redisconfig = {
                host: redisconfig.host,
                port: redisconfig.port || 6379,
                password: redisconfig.pwd || undefined,
                db: redisconfig.dbindex || 0
            };

            try {
                var client = redis.createClient(redisconfig);
                __do(null, client, callback);
            }
            catch (e) {
                __do(e, undefined, callback);
            }


        }
    }
};

__redisutil.getClient = function (configKey, callback) {


    var redisconfig;
    var client = null;

    //回掉模式
    if (callback && typeof callback === 'function') {

        if (!configKey || configKey.length === 0) {
            callback(new Error('configKey is empty'));
            return;
        }

        redisconfig = __appsetting.getItem(configKey);
        if (!redisconfig) {
            callback(new Error(`redisconfig ${configKey} not exist`));
            return;
        }

        if (typeof redisconfig === 'string') {
            client = redis.createClient(redisconfig);
            client.on("error", function (err) { console.log("[redis error] " + err); });
            callback(null, client);
        }
        else {
            if (!redisconfig.host) {
                try {
                    callback(new Error(`redisconfig ${configKey} host error`));
                }
                catch (e) {
                    callback(e);
                }
            } else {

                redisconfig = {
                    host: redisconfig.host,
                    port: redisconfig.port || 6379,
                    password: redisconfig.pwd || redisconfig.password || undefined,
                    db: redisconfig.dbindex || redisconfig.db || 0
                };

                try {
                    client = redis.createClient(redisconfig);
                    client.on("error", function (err) { console.log("[redis error] " + err); });
                    callback(null, client);
                }
                catch (e) {
                    callback(e);
                }
            }
        }
    }
    else {//非回调模式

        if (!configKey || configKey.length === 0) {
            throw new Error('configKey is empty');
        }

        redisconfig = __appsetting.getItem(configKey);
        if (!redisconfig) {
            throw new Error(`redisconfig ${configKey} not exist`);
        }

        if (typeof redisconfig === 'string') {
            client = redis.createClient(redisconfig);
            client.on("error", function (err) { console.log("[redis error] " + err); });
            return client;
        }
        else {
            if (!redisconfig.host) {
                throw new Error(`redisconfig ${configKey} host error`);
            } else {

                redisconfig = {
                    host: redisconfig.host,
                    port: redisconfig.port || 6379,
                    password: redisconfig.pwd || redisconfig.password || undefined,
                    db: redisconfig.dbindex || redisconfig.db || 0
                };

                client = redis.createClient(redisconfig);
                client.on("error", function (err) { console.log("[redis error] " + err); });
                return cli;
            }
        }

    }
};

__redisutil.redis = redis;

module.exports = __redisutil;