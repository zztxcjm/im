CREATE DATABASE `imdb` /*!40100 DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci */;

use imdb;

CREATE TABLE `im_message` (
	`Id` BIGINT(20) NOT NULL AUTO_INCREMENT,
	`MsgUid` CHAR(36) NOT NULL,
	`MsgType` TINYINT(4) NOT NULL,
	`SenderUserType` TINYINT(4) NOT NULL,
	`SenderUserId` VARCHAR(50) NOT NULL,
	`SendTime` DATETIME NOT NULL,
	`Body` VARCHAR(5000) NOT NULL COLLATE 'utf8mb4_general_ci',
	`ExtInfo` VARCHAR(5000) NULL DEFAULT NULL,
	PRIMARY KEY (`Id`),
	INDEX `idx_im_message_MsgUid` (`MsgUid`),
	INDEX `idx_im_message_MsgType_SenderUserType` (`MsgType`, `SenderUserType`, `SenderUserId`)
)
COLLATE='utf8_general_ci'
ENGINE=InnoDB
;

CREATE TABLE `im_message_receivers` (
	`Id` BIGINT(20) NOT NULL AUTO_INCREMENT,
	`MsgUid` CHAR(36) NOT NULL,
	`ReceiverUserId` VARCHAR(50) NOT NULL,
	PRIMARY KEY (`Id`),
	INDEX `idx_im_message_receivers_MsgUid` (`MsgUid`)
)
COLLATE='utf8_general_ci'
ENGINE=InnoDB
;
