using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SQLite;

namespace Pixiv_Background
{
    //数据库的版本升级代码 用于旧版数据库内容升级成新版的数据库
    internal class dbPatcher
    {
        private static void _updateDatabase_100_to_101(SQLiteConnection connection)
        {
            var trans = connection.BeginTransaction();
            var cmd = new SQLiteCommand(connection);
            var insert_col = "ALTER TABLE Illust ADD COLUMN Tool VARCHAR";
            var clear_all_http = "UPDATE Illust SET HTTP_Status=0";
            cmd.CommandText = insert_col;
            cmd.ExecuteNonQuery();
            cmd.CommandText = clear_all_http;
            cmd.ExecuteNonQuery();

            var edit_version = "UPDATE DbVars SET Value='1.0.1' WHERE Key='Version'";
            cmd.CommandText = edit_version;
            cmd.ExecuteNonQuery();
            trans.Commit();
        }
        private static void _updateDatabase_101_to_102(SQLiteConnection connection)
        {
            var trans = connection.BeginTransaction();
            var cmd = new SQLiteCommand(connection);
            var insert_col1 = "ALTER TABLE User ADD COLUMN Home_Page VARCHAR";
            var insert_col2 = "ALTER TABLE User ADD COLUMN Gender VARCHAR";
            var insert_col3 = "ALTER TABLE User ADD COLUMN Personal_Tags VARCHAR";
            var insert_col4 = "ALTER TABLE User ADD COLUMN Address VARCHAR";
            var insert_col5 = "ALTER TABLE User ADD COLUMN Birthday VARCHAR";
            var insert_col6 = "ALTER TABLE User ADD COLUMN Twitter VARCHAR";
            cmd.CommandText = insert_col1;
            cmd.ExecuteNonQuery();
            cmd.CommandText = insert_col2;
            cmd.ExecuteNonQuery();
            cmd.CommandText = insert_col3;
            cmd.ExecuteNonQuery();
            cmd.CommandText = insert_col4;
            cmd.ExecuteNonQuery();
            cmd.CommandText = insert_col5;
            cmd.ExecuteNonQuery();
            cmd.CommandText = insert_col6;
            cmd.ExecuteNonQuery();

            //deleting Favor in Illust
            var create_tmp_table = "CREATE TEMPORARY TABLE Temp_Illust (ID INT, Author_ID INT, Title VARCHAR, Description TEXT, Tag VARCHAR, Tool VARCHAR, Click INT, Submit_Time BIGINT, HTTP_Status INT, Last_Update BIGINT)";
            cmd.CommandText = create_tmp_table;
            cmd.ExecuteNonQuery();
            var trans_tmp_table = "INSERT INTO Temp_Illust SELECT ID, Author_ID, Title, Description, Tag, Tool, Click, Submit_Time, HTTP_Status, Last_Update FROM Illust";
            cmd.CommandText = trans_tmp_table;
            cmd.ExecuteNonQuery();
            var drop_origin_table = "DROP TABLE Illust";
            cmd.CommandText = drop_origin_table;
            cmd.ExecuteNonQuery();
            var create_origin_Table = "CREATE TABLE Illust(ID INT PRIMARY KEY, Author_ID INT NOT NULL, Title VARCHAR, Description TEXT, Tag VARCHAR, Tool VARCHAR, Click INT NOT NULL DEFAULT 0, Rate_Count INT NOT NULL DEFAULT 0, Score INT NOT NULL DEFAULT 0, Width INT NOT NULL DEFAULT 0, Height INT NOT NULL DEFAULT 0, Submit_Time BIGINT NOT NULL, HTTP_Status INT NOT NULL, Last_Update BIGINT NOT NULL)";
            cmd.CommandText = create_origin_Table;
            cmd.ExecuteNonQuery();
            var trans_origin_table = "INSERT INTO Illust(ID, Author_ID, Title, Description, Tag, Tool, Click, Submit_Time, HTTP_Status, Last_Update) SELECT ID, Author_ID, Title, Description, Tag, Tool, Click, Submit_Time, HTTP_Status, Last_Update FROM Temp_Illust";
            cmd.CommandText = trans_origin_table;
            cmd.ExecuteNonQuery();
            var drop_temp_table = "DROP TABLE Temp_Illust";
            cmd.CommandText = drop_temp_table;
            cmd.ExecuteNonQuery();

            //setting all values to un-init status
            var set_user = "UPDATE User SET HTTP_Status=0";
            var set_illust = "UPDATE Illust SET HTTP_Status=0";
            cmd.CommandText = set_user;
            cmd.ExecuteNonQuery();
            cmd.CommandText = set_illust;
            cmd.ExecuteNonQuery();

            var update_version = "UPDATE DbVars SET Value='1.0.2' WHERE Key='Version'";
            cmd.CommandText = update_version;
            cmd.ExecuteNonQuery();
            trans.Commit();
        }
        private static void _updateDatabase_102_to_103(SQLiteConnection connection)
        {
            var trans = connection.BeginTransaction();
            var cmd = new SQLiteCommand(connection);

            var illust_add_row = "ALTER TABLE Illust ADD COLUMN Last_Success_Update BIGINT NOT NULL DEFAULT 0";
            var user_add_row = "ALTER TABLE User ADD COLUMN Last_Success_Update BIGINT NOT NULL DEFAULT 0";
            cmd.CommandText = illust_add_row;
            cmd.ExecuteNonQuery();
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();

            var copy_illust_timestamp = "UPDATE Illust SET Last_Success_Update = Last_Update WHERE HTTP_Status = 200";
            var copy_user_timestamp = "UPDATE User SET Last_Success_Update = Last_Update WHERE HTTP_Status = 200";
            cmd.CommandText = copy_illust_timestamp;
            cmd.ExecuteNonQuery();
            cmd.CommandText = copy_user_timestamp;
            cmd.ExecuteNonQuery();

            var edit_version = "UPDATE DbVars SET Value='1.0.3' WHERE Key='Version'";
            cmd.CommandText = edit_version;
            cmd.ExecuteNonQuery();

            trans.Commit();
        }
        private static void _updateDatabase_103_to_104(SQLiteConnection connection)
        {
            var trans = connection.BeginTransaction();
            var cmd = new SQLiteCommand(connection);

            var illust_add_row = "ALTER TABLE Illust ADD COLUMN Page INT NOT NULL DEFAULT 1";
            cmd.CommandText = illust_add_row;
            cmd.ExecuteNonQuery();

            var edit_version = "UPDATE DbVars SET Value='1.0.4' WHERE Key='Version'";
            cmd.CommandText = edit_version;
            cmd.ExecuteNonQuery();

            trans.Commit();
        }
        private static void _updateDatabase_104_to_105(SQLiteConnection connection)
        {
            var trans = connection.BeginTransaction();
            var cmd = new SQLiteCommand(connection);

            var illust_add_row = "ALTER TABLE Illust ADD COLUMN Origin TINYINT NOT NULL DEFAULT 0";
            cmd.CommandText = illust_add_row;
            cmd.ExecuteNonQuery();

            var edit_version = "UPDATE DbVars SET Value='1.0.5' WHERE Key='Version'";
            cmd.CommandText = edit_version;
            cmd.ExecuteNonQuery();

            trans.Commit();
        }
        private static void _updateDatabase_105_to_106(SQLiteConnection connection)
        {
            var trans = connection.BeginTransaction();
            var cmd = new SQLiteCommand(connection);

            var illust_add_row = "ALTER TABLE Illust ADD COLUMN Bookmark_Count INT NOT NULL DEFAULT 0";
            cmd.CommandText = illust_add_row;
            cmd.ExecuteNonQuery();
            illust_add_row = "ALTER TABLE Illust ADD COLUMN Comment_Count INT NOT NULL DEFAULT 0";
            cmd.CommandText = illust_add_row;
            cmd.ExecuteNonQuery();

            var user_add_row = "ALTER TABLE User ADD COLUMN Job VARCHAR";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();
            user_add_row = "ALTER TABLE User ADD COLUMN Follow_Users INT";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();
            user_add_row = "ALTER TABLE User ADD COLUMN Follower INT";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();
            user_add_row = "ALTER TABLE User ADD COLUMN Illust_Bookmark_Public INT";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();
            user_add_row = "ALTER TABLE User ADD COLUMN Mypixiv_Users INT";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();
            user_add_row = "ALTER TABLE User ADD COLUMN Total_Illusts INT";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();
            user_add_row = "ALTER TABLE User ADD COLUMN Total_Novels INT";
            cmd.CommandText = user_add_row;
            cmd.ExecuteNonQuery();

            var edit_version = "UPDATE DbVars SET Value='1.0.6' WHERE Key='Version'";
            cmd.CommandText = edit_version;
            cmd.ExecuteNonQuery();

            trans.Commit();
        }
        public static void Patch(string from_version, string to_version, SQLiteConnection connection)
        {
            if (from_version == "1.0.0")
            {
                _updateDatabase_100_to_101(connection);
                from_version = "1.0.1";
            }
            if (from_version == "1.0.1")
            {
                _updateDatabase_101_to_102(connection);
                from_version = "1.0.2";
            }
            if (from_version == "1.0.2")
            {
                _updateDatabase_102_to_103(connection);
                from_version = "1.0.3";
            }
            if (from_version == "1.0.3")
            {
                _updateDatabase_103_to_104(connection);
                from_version = "1.0.4";
            }
            if (from_version == "1.0.4")
            {
                _updateDatabase_104_to_105(connection);
                from_version = "1.0.5";
            }
            if (from_version == "1.0.5")
            {
                _updateDatabase_105_to_106(connection);
                from_version = "1.0.6";
            }
        }
    }
}
