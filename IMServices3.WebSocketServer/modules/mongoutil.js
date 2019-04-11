const mongodb = require('mongodb');

const __appsetting = require('./appsetting.js');
const __defaultdb = 'defaultdb';
const __mongoutil = {
    connCount: 0,
    funcproxys: ['insert', 'insertMany', 'update', 'updateOne', 'find', 'findOne', 'drop', 'remove', 'aggregate']
};

var __do = function (err, dbconn, collection, callback) {

    if (err) {
        __closedb(dbconn);
        console.log(err);
    }
    if (callback && typeof callback === 'function') {
        var ad = null;
        if (!err) {
            ad = new __dbAdptor(dbconn, collection);
            __dbAdptorPools[ad.Id] = ad;
            __mongoutil.connCount++;
            console.log(`mongodb conn ${ad.Id} is connected. conn totalcount is ${__mongoutil.connCount} at in process`);
        }
        callback(err, ad);
    }
};
var __closedb = function (db) {
    if (db && typeof db['close'] === 'function') {
        try {
            db.close();
        }
        catch (e) {
            console.log(e);
        }
        __mongoutil.connCount--;
        if (__mongoutil.connCount < 0)
            __mongoutil.connCount = 0;
    }
};
//====适配器维护线程 开始====
var __dbAdptorPools = {};
var __dbAdptorCloseChanged = false;
var __dbAdptorPools_timerhandler = function () {
    try {
        for (var key in __dbAdptorPools) {
            var ad = __dbAdptorPools[key];
            if (typeof ad["lastAccessTime"] !== 'undefined') {
                if (ad.autoRelease) {
                    //超过10s未调用或waitRelease为true就是无效的待释放的ad
                    if ((new Date() - ad.lastAccessTime) / 1000 >= 10 || ad.waitRelease) {
                        ad.close();
                        delete __dbAdptorPools[key];
                    }
                }
            }
        }
        if (__dbAdptorCloseChanged) {
            console.log(`conn totalcount is ${__mongoutil.connCount} at in process`);
            __dbAdptorCloseChanged = false;
        }
        //console.log('function adptorPools_timerhandler execute success.');
    }
    catch (e) {
        console.log('function adptorPools_timerhandler execute exception. ' + e);
    }

    setTimeout(__dbAdptorPools_timerhandler, 5000);

};
setTimeout(__dbAdptorPools_timerhandler, 5000);
//====适配器维护线程 结束====

var __dbAdptor = function () {

    var __getDbAdptorFuncProxy = function (funcname, self) {
        return function () {
            var args = [funcname];
            for (var i in arguments) args.push(arguments[i]);
            return self.exec.apply(self, args);
        };
    };

    return function (db, col) {

        //适配器ID
        this.Id = undefined;
        while (!this.Id || typeof __dbAdptorPools[this.Id] !== 'undefined') {
            this.Id = 'dbAdptor_' + Math.round(Math.random() * 10000000);
        }

        //最后访问适配器的时间
        this.lastAccessTime = new Date();

        //是否自动释放适配器
        this.autoRelease = true;

        //适配器是否正等待释放,autoRelease=true此参数生效
        this.waitRelease = false;

        //适配器是否已被释放,autoRelease=true此参数生效
        this.isReleased = false;

        //数据连接
        this.db = db;

        //集合对象
        this.collection = col;

        //对collection对象中的接口进行代理
        var funcproxys = __mongoutil.funcproxys;
        if (funcproxys && typeof funcproxys["length"] !== "undefined" && funcproxys.length > 0) {
            for (var i in funcproxys) this[funcproxys[i]] = __getDbAdptorFuncProxy(funcproxys[i], this);
        }

        //关闭连接
        this.close = function () {

            this.isReleased = true;

            __closedb(this.db);

            delete __dbAdptorPools[this.Id];
            __dbAdptorCloseChanged = true;

            console.log(`mongodb conn ${this.Id} is closed`);

        };

        //执行collection中的接口函数
        this.exec = function () {

            var args = [];
            for (var i in arguments) {
                var p = arguments[i];
                args.push(p);
            }

            var fn;
            if (args.length === 0) {
                return;
            }
            else if (args.length === 1) {
                fn = args[0];
                if (typeof this.collection[fn] === 'function') {

                    try {
                        if (!this.isReleased) {
                            this.lastAccessTime = new Date();
                        }
                        return this.collection[fn].apply(this.collection);
                    }
                    catch (e) {
                        this.close();
                        console.log(e);
                    }

                }
            }
            else if (args.length > 1) {
                fn = args[0];
                if (typeof this.collection[fn] === 'function') {

                    try {
                        var appArgs = args.slice(1);
                        if (this.isReleased) {
                            appArgs[0] = new Error('adptor is released');
                        } else {
                            this.lastAccessTime = new Date();
                        }
                        return this.collection[fn].apply(this.collection, appArgs);
                    }
                    catch (e) {
                        this.close();
                        console.log(e);
                    }

                }
            }

        };

    };


}();

__mongoutil.act = function (entstr, configKey, callback) {

    //第二个参数传的是回掉
    if (typeof configKey === 'function') {
        callback = configKey;
        configKey = __defaultdb;
    }
    else {
        if (!callback || typeof callback !== 'function') {
            //没传回掉或回掉不是一个函数
            console.log('callback not is a function');
            return;
        }
        if (!configKey || configKey.length === 0) {
            configKey = __defaultdb;
        }
    }
    //entstr
    if (!entstr || entstr.length === 0) {
        __do(new Error('entstr is empty'), undefined, undefined, callback);
        return;
    }
    var _curDbName, _curColName, arr = entstr.split('.');
    if (arr.length === 0) {
        __do(new Error('entString format error'), undefined, undefined, callback);
        return;
    }
    else if (arr.length === 1) {
        _curDbName = arr[0];
    }
    else {
        _curDbName = arr[0];
        _curColName = entstr.substr(entstr.indexOf('.') + 1);
    }
    //collection name validate
    if (!_curColName || _curColName.length === 0) {
        __do(new Error('entString not include collectionName'), undefined, undefined, callback);
        return;
    }

    //dbconfig validate
    var dbconfig = __appsetting.getItem(configKey);
    if (!dbconfig) {
        __do(new Error('mongodb config not exist'), undefined, undefined, callback);
    }
    else {

        var conn_str;
        if (typeof dbconfig === 'string') {
            conn_str = dbconfig.replace(/\{dbname\}/gi, _curDbName);
        } else {

            if (!dbconfig.host) {
                __do(new Error('mongodb config host error'), undefined, undefined, callback);
                return;
            }

            //拼连接串
            //mongodb://mongouser:******@10.66.230.89:27017/
            var needAuth = typeof dbconfig.user !== 'undefined' && dbconfig.user.length > 0;
            needAuth = needAuth && typeof dbconfig.pwd !== 'undefined' && dbconfig.pwd.length > 0;

            conn_str = needAuth
                ? `mongodb://${dbconfig.user}:${dbconfig.pwd}@${dbconfig.host}:${dbconfig.port || 27017}/${_curDbName}`
                : `mongodb://${dbconfig.host}:${dbconfig.port || 27017}/${_curDbName}`;

            if (typeof dbconfig.authSource !== 'undefined' && dbconfig.authSource.length > 0) {
                conn_str += `?authSource=${dbconfig.authSource}`;
            }

        }

        mongodb.MongoClient.connect(conn_str, {}, function (connerr, db) {
            if (connerr) {
                __do(connerr, undefined, undefined, callback);
            }
            else if (!db) {
                __do(new Error('db ' + _curDbName + ' error'), undefined, undefined, callback);
            }
            else {
                db.collection(_curColName, function (err, col) {
                    __do(err, db, col, callback);
                });
            }
        });

    }

};

__mongoutil.objid = {

    ObjectID: mongodb.ObjectID

    , fromString: function (str) {
        if (typeof str === 'string' && str.length > 0)
            return mongodb.ObjectID.createFromHexString(str);
        else
            return undefined;
    }

    , getTime: function (objid) {
        if (mongodb.ObjectID.isValid(objid))
            return objid.getTimestamp();
        return undefined;
    }

    , toString: function (objid) {
        if (mongodb.ObjectID.isValid(objid))
            return objid.toHexString();
        return '';
    }

};

module.exports = __mongoutil;