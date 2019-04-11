var util = {};
var __emptyString = '';
var __trimReg = /(^\s*)|(\s*$)/g;

util.emptyFn = function () { };
util.emptyString = __emptyString;
util.isEmpty = function (str) { return str === undefined || str === null || str.length === 0; };

util.trim = function (s) {
    return s.replace(__trimReg, __emptyString);
};
util.trimStart = function (s1, s2) {

    if (!s1 || !s2)
        return s1;

    var s1len = s1.length, s2len = s2.length;

    if (s1len === 0 || s2len === 0 || s1len < s2len) return s1;
    if (s1len === s2len) return __emptyString;
    if (s1len > s2len) return s1.replace(new RegExp('^' + s2, 'i'), __emptyString);

};
util.trimEnd = function (s1, s2) {

    if (!s1 || !s2)
        return s1;

    var s1len = s1.length, s2len = s2.length;

    if (s1len === 0 || s2len === 0 || s1len < s2len) return s1;
    if (s1len === s2len) return __emptyString;
    if (s1len > s2len) return s1.replace(new RegExp(s2 + '$', 'i'), __emptyString);

};
util.startWith = function (s1, s2) {

    if (!s1 || !s2)
        return false;

    var s1len = s1.length, s2len = s2.length;

    if (s1len === 0 || s2len === 0 || s1len < s2len) return false;
    if (s1len === s2len) return s1.toLowerCase() === s2.toLowerCase();
    if (s1len > s2len) return s1.substring(0, s2len).toLowerCase() === s2.toLowerCase();

};
util.endWith = function (s1, s2) {

    if (!s1 || !s2)
        return false;

    var s1len = s1.length, s2len = s2.length;

    if (s1len === 0 || s2len === 0 || s1len < s2len) return false;
    if (s1len === s2len) return s1.toLowerCase() === s2.toLowerCase();
    if (s1len > s2len) return s1.substr(s1len - s2len).toLowerCase() === s2.toLowerCase();

};

util.toUnixTime = function (date) {

    return Math.round(date.getTime() / 1000);

};

util.outJson = function (obj, res) {

    if (!obj)
        return;
    if (!res)
        return;

    res.status(200).json({
        error: null,
        value: obj
    });

};
util.wait = function (continueFn, completedFn, timeoutFn, interval, timeout) {

    if (typeof continueFn !== 'function')
        return;

    interval = typeof interval !== 'number' ? 500 : interval;
    timeout = typeof timeout !== 'number' ? 10000 : timeout;

    var waitSeconds = interval;
    var handler = function () {

        if (waitSeconds >= timeout) {
            if (typeof timeoutFn === 'function')
                timeoutFn();
        } else {
            if (continueFn()) {
                waitSeconds += interval;
                setTimeout(handler, interval);
            } else {
                if (typeof completedFn === 'function')
                    completedFn();
            }
        }
    };
    setTimeout(handler, interval);
};

module.exports = util;