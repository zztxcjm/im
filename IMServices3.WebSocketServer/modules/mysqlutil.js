const mysql = require('mysql');
const appsetting = require('./appsetting.js');

var __mysqlutil = {};
__mysqlutil.act = function (configKey, callback) {

    if (callback && typeof callback === 'function') {

        if (!configKey || configKey.length === 0) {
            callback(new Error('configKey is empty'));
            return;
        }
        var cfg = appsetting.getItem(configKey);
        if (!cfg) {
            callback(new Error(`mysqlconfig ${configKey} not exist`));
            return;
        }

        if (typeof cfg === 'string') {
            callback(null, mysql.createPool(cfg));
        } else {
            callback(null, mysql.createPool(cfg));
        }

    }
};

module.exports = __mysqlutil;