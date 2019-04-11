这个一个基于redis和Websocket实现的即时消息系统，可在微信小程序和Web页中实现即时聊天和即时消息推送。
这个项目是完全开源了，你可以随意使用和修改。

微信：ipcheat
邮箱：cuiming@yeah.net

主要技术
c#，redis，websocket，nodejs，mysql

项目结构
IMServices3-是IM系统的WebAPI
IMServices3.DataAccessor-处理和mysql相关的逻辑
IMServices3.Entity-服务器端的实体定义
IMServices3.Util-常用方法
IMServices3.PostService-是一个服务器程序，负责消息的异步投递
IMServices3.Sdk-在服务器端调用IM系统的sdk
IMServices3/ClientSdk-在web端和小程序端调用im系统的sdk
IMServices3.WebSocketServer-一个用nodejs实现的服务器程序，负责管理客户端的连接和负责将消息投递到客户端