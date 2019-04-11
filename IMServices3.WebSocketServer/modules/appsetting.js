var fs = require('fs');
var __emptyString = '';
var __projectconfigname = 'config.json';
var __globalConfigUrl = 'https://api.handday.com/common/appsetting';
var __globalConfigCache = process.__globalConfigCache || {};
var __projectconfig = process.__projectconfig || null;

var buildConfigCache = function (globalConfigUrl) {

    var res = require('sync-request')(
        'POST', globalConfigUrl || __globalConfigUrl, { 'headers': { 'X-Request-With': 'AppSettingGeter' } });

    if (res.statusCode !== 200) {
        console.log('HttpError Code:' + res.statusCode);
    }
    else {
        var config = {}, body = res.getBody('utf8');
        if (body.length > 0) {
            var line, arr = body.split('\n'), trim = require('./utils.js').trim;
            for (var i in arr) {
                line = arr[i];
                if (line !== __emptyString && line !== '\r' && line.substring(0, 1) !== '#') {
                    var index = line.indexOf(':');
                    var k = line.substring(0, index);
                    var v = line.substr(index + 1);
                    config[trim(k)] = trim(v);
                }
            }
        }

        __globalConfigCache = process.__globalConfigCache = {
            configObj: config,
            configString: body,
            lastAccessTime: new Date()
        };

    }

};

var queryGlobalConfig = function (key, globalConfigUrl) {

    var lastAccessTime = __globalConfigCache['lastAccessTime'];
    var configString = __globalConfigCache['configString'];
    var configObj = __globalConfigCache['configObj'];


    if (typeof lastAccessTime === 'undefined'
        || typeof configString === 'undefined'
        || typeof configObj === 'undefined') {
        buildConfigCache(globalConfigUrl);
    }
    else if ((new Date() - lastAccessTime) / 1000 > 300) {
        buildConfigCache(globalConfigUrl);
    }

    if (typeof __globalConfigCache.configObj === 'undefined')
        return __emptyString;

    var val = __globalConfigCache.configObj[key];
    if (typeof val === 'undefined')
        return __emptyString;
    else
        return val;

};

var getConfigFile = function () {

    var path = require('path');
    var dir = __dirname, s = '', arr = [];
    dir.split(path.sep).forEach(function (element, i) {
        s += element + path.sep;
        arr.unshift(path.join(s, __projectconfigname));
    });

    var re;
    arr.forEach(function (element, i) {
        if (fs.existsSync(element)) {
            re = element;
            return false;
        }
        return true;
    });

    return re;

};

var initFromConfigJSON = function () {

    try {
        var s = fs.readFileSync(getConfigFile(), 'utf-8');
        if (s.length > 0) {
            var c = JSON.parse(s);
            if (c) {
                c.lastReadFileTime = new Date();
                __projectconfig = process.__projectconfig = c;
            }
            return;
        }

    } catch (error) {
        console.error(error);
    }

    __projectconfig = process.__projectconfig = {};
};

module.exports = {

    getItem: function (key, globalConfigUrl) {

        if (!key || key.length === 0)
            return __emptyString;

        //__projectconfig 如果为空或最后访问时间为空或最后访问大于60s就重新读取文件 
        if (!__projectconfig
            || typeof __projectconfig['lastReadFileTime'] === undefined
            || (new Date() - __projectconfig['lastReadFileTime']) / 1000 > 60) {
            initFromConfigJSON();
        }

        if (__projectconfig) {
            var v = __projectconfig[key];
            if (typeof v !== 'undefined')
                return v;
        }

        return queryGlobalConfig(key, globalConfigUrl);

    }

};