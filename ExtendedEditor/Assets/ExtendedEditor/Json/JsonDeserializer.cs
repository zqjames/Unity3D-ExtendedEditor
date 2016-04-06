﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TNRD.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace TNRD.Editor.Json {

    public class JsonDeserializer {

        public static object Deserialize( string json, Type type ) {
            if ( string.IsNullOrEmpty( json ) ) return null;

            var deserializer = new JsonDeserializer();
            var obj = deserializer.DeserializeObject( json, type );
            return obj;
        }

        public static T Deserialize<T>( string json ) where T : class {
            if ( string.IsNullOrEmpty( json ) ) return null;

            var deserializer = new JsonDeserializer();
            var obj = deserializer.DeserializeObject( json, typeof( T ) );
            return (T)obj;
        }

        private List<JsonType> jsonTypes = new List<JsonType>();
        private List<Type> windowTypes = new List<Type>();

        private object DeserializeObject( string json, Type type ) {
            var index = 0;

            ReadJsonTypes( ref json );
            json = json.Remove( 0, 1 );

            foreach ( var item in jsonTypes ) {
                var jType = item.LoadType();
                if ( jType.IsSubclassOf( typeof( ExtendedWindow ) ) ) {
                    windowTypes.Add( jType );
                }
            }

            var jObj = (JsonObject)ReadObject( json, ref index );
            var obj = CreateObject( jObj );

            return Convert.ChangeType( obj, type );
        }

        private ExtendedEditor GetEditorInstance( Type editorType ) {
            var windowsField = typeof( ExtendedEditor ).GetField( "serializedWindowTypes", BindingFlags.Instance | BindingFlags.NonPublic );

            var objects = Resources.FindObjectsOfTypeAll<ExtendedEditor>();
            if ( objects.Length > 0 ) {
                foreach ( var editor in objects ) {
                    var eWindows = (List<JsonType>)windowsField.GetValue( editor );
                    var foundEditor = true;

                    foreach ( var w1 in eWindows ) {
                        var foundWindow = false;
                        foreach ( var w2 in windowTypes ) {
                            if ( w1.LoadType() == w2 ) {
                                foundWindow = true;
                                break;
                            }
                        }

                        if ( !foundWindow ) {
                            foundEditor = false;
                            break;
                        }
                    }

                    if ( foundEditor ) {
                        return editor;
                    }
                }
            }

            return (ExtendedEditor)ScriptableObject.CreateInstance( editorType );
        }

        private object CreateObject( JsonObject obj ) {
            var tid = int.Parse( (string)obj.KeyValues["$typeid"] );
            var type = jsonTypes[tid].LoadType();
            object instance = null;

            if ( type.IsSubclassOf( typeof( EditorWindow ) ) ) {
                instance = GetEditorInstance( type );
            } else {
                instance = Activator.CreateInstance( type );
            }

            foreach ( var item in obj.KeyValues.Keys ) {
                if ( item == "$typeid" ) continue;
                var value = obj.KeyValues[item];

                if ( value == null ) {
                    SetValue( type, instance, item, null );
                    continue;
                }

                var valueType = value.GetType();

                if ( valueType == typeof( JsonArray ) ) {
                    valueType = GetMemberType( type, item );
                    valueType = GetListType( valueType );

                    var jValue = CreateArray( (JsonArray)value, valueType );
                    SetValue( type, instance, item, jValue );
                } else if ( valueType == typeof( JsonObject ) ) {
                    var jValue = CreateObject( (JsonObject)value );
                    SetValue( type, instance, item, jValue );
                } else {
                    valueType = GetMemberType( type, item );

                    var stringValue = (string)value;
                    if ( valueType == typeof( bool ) ) {
                        SetValue( type, instance, item, bool.Parse( stringValue ) );
                    } else if ( valueType == typeof( int ) ) {
                        SetValue( type, instance, item, int.Parse( stringValue ) );
                    } else if ( valueType == typeof( float ) ) {
                        SetValue( type, instance, item, float.Parse( stringValue ) );
                    } else if ( valueType == typeof( double ) ) {
                        SetValue( type, instance, item, double.Parse( stringValue ) );
                    } else if ( valueType == typeof( decimal ) ) {
                        SetValue( type, instance, item, decimal.Parse( stringValue ) );
                    } else if ( valueType == typeof( string ) ) {
                        SetValue( type, instance, item, value );
                    } else if ( valueType.IsEnum ) {
                        var eval = Enum.Parse( valueType, stringValue, true );
                        SetValue( type, instance, item, eval );
                    }
                }
            }

            return instance;
        }

        private object CreateArray( JsonArray obj, Type itemType ) {
            IList list = (IList)Activator.CreateInstance(
            typeof( List<> ).MakeGenericType( itemType ) );

            foreach ( var item in obj.KeyValues.Keys ) {
                var value = obj.KeyValues[item];

                if ( value == null ) {
                    list.Add( null );
                    continue;
                }

                var valueType = value.GetType();

                if ( valueType == typeof( JsonArray ) ) {
                    // Not able to do this :D
                } else if ( valueType == typeof( JsonObject ) ) {
                    var jValue = CreateObject( (JsonObject)value );
                    list.Add( jValue );
                } else {
                    valueType = itemType;

                    var stringValue = (string)value;
                    if ( valueType == typeof( bool ) ) {
                        list.Add( bool.Parse( stringValue ) );
                    } else if ( valueType == typeof( int ) ) {
                        list.Add( int.Parse( stringValue ) );
                    } else if ( valueType == typeof( float ) ) {
                        list.Add( float.Parse( stringValue ) );
                    } else if ( valueType == typeof( double ) ) {
                        list.Add( double.Parse( stringValue ) );
                    } else if ( valueType == typeof( decimal ) ) {
                        list.Add( decimal.Parse( stringValue ) );
                    } else if ( valueType == typeof( string ) ) {
                        list.Add( value );
                    } else if ( valueType.IsEnum ) {
                        var eval = Enum.Parse( valueType, stringValue, true );
                        list.Add( eval );
                    }
                }
            }

            return list;
        }

        private void SetValue( Type type, object instance, string name, object value ) {
            var field = JsonHelper.GetField( type, name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
            if ( field != null ) {
                field.SetValue( instance, value );
                return;
            }

            var property = JsonHelper.GetProperty( type, name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
            if ( property != null ) {
                if ( property.CanWrite ) {
                    property.SetValue( instance, value, null );
                }
            }
        }

        private Type GetListType( Type type ) {
            foreach ( Type intType in type.GetInterfaces() ) {
                if ( intType.IsGenericType
                    && intType.GetGenericTypeDefinition() == typeof( IList<> ) ) {
                    return intType.GetGenericArguments()[0];
                }
            }

            return null;
        }

        private Type GetMemberType( Type type, string name ) {
            var member = JsonHelper.GetMember( type, name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

            if ( member == null ) return null;

            if ( member.MemberType == MemberTypes.Field ) {
                return ( member as FieldInfo ).FieldType;
            } else if ( member.MemberType == MemberTypes.Property ) {
                return ( member as PropertyInfo ).PropertyType;
            }

            return null;
        }

        private object ReadObject( string json, ref int index ) {
            var i = 0;

            var jObject = new JsonObject();

            var currentName = "";
            object currentObjectValue = null;
            var currentStringValue = "";

            var inQuotes = false;

            // Partially taken from SimpleJson
            while ( i < json.Length ) {
                switch ( json[i] ) {
                    case '{':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        } else {
                            currentObjectValue = ReadObject( json.Substring( i + 1 ), ref i );
                        }
                        break;
                    case '[':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        } else {
                            currentObjectValue = ReadArray( json.Substring( i + 1 ), ref i );
                        }
                        break;
                    case ':':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        } else {
                            currentName = currentStringValue;
                            currentStringValue = "";
                            currentObjectValue = null;
                        }
                        break;
                    case '"': {
                            inQuotes ^= true;
                        }
                        break;
                    case ',':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        } else {
                            // Only add when it doesn't have to prevent errors, no idea why this is happening though
                            if ( !jObject.KeyValues.ContainsKey( currentName ) ) {
                                jObject.KeyValues.Add( currentName, currentObjectValue == null ? currentStringValue : currentObjectValue );
                            }

                            currentName = "";
                            currentStringValue = "";
                            currentObjectValue = null;
                        }
                        break;
                    case '}':
                        if ( currentName != null ) {
                            // Only add when it doesn't have to prevent errors, no idea why this is happening though
                            if ( !jObject.KeyValues.ContainsKey( currentName ) ) {
                                jObject.KeyValues.Add( currentName, string.IsNullOrEmpty( currentStringValue ) ? currentObjectValue : currentStringValue );
                            }

                            currentName = "";
                            currentStringValue = "";
                            currentObjectValue = null;
                        }
                        index += i + 1;
                        return jObject;
                    case ' ':
                    case '\t':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        }
                        break;
                    case '\\': {
                            i++;
                            if ( inQuotes ) {
                                switch ( json[i] ) {
                                    case 't':
                                        currentStringValue += '\t';
                                        break;
                                    case 'r':
                                        currentStringValue += '\r';
                                        break;
                                    case 'n':
                                        currentStringValue += '\n';
                                        break;
                                    case 'b':
                                        currentStringValue += '\b';
                                        break;
                                    case 'f':
                                        currentStringValue += '\f';
                                        break;
                                    case 'u':
                                        var s = json.Substring( i + 1, 4 );
                                        currentStringValue += (char)int.Parse( s, System.Globalization.NumberStyles.AllowHexSpecifier );
                                        i += 4;
                                        break;
                                    default:
                                        currentStringValue += json[i];
                                        break;
                                }
                            }
                        }
                        break;
                    default:
                        currentStringValue += json[i];
                        break;
                }
                i++;
            }

            index += i;
            return jObject;
        }

        private object ReadArray( string json, ref int index ) {
            var i = 0;
            var inQuotes = false;

            var jObject = new JsonArray();

            var currentIndex = 0;
            object currentObjectValue = null;
            var currentStringValue = "";

            while ( i < json.Length ) {
                switch ( json[i] ) {
                    case '{':
                        currentObjectValue = ReadObject( json.Substring( i + 1 ), ref i );
                        break;
                    case '[':
                        currentObjectValue = ReadArray( json.Substring( i + 1 ), ref i );
                        break;
                    case ',':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        } else {
                            jObject.KeyValues.Add( currentIndex.ToString(), currentObjectValue == null ? currentStringValue : currentObjectValue );
                            currentIndex++;
                            currentStringValue = "";
                            currentObjectValue = null;
                        }
                        break;
                    case '"':
                        inQuotes ^= true;
                        break;
                    case ']':
                        if ( currentObjectValue != null || !string.IsNullOrEmpty( currentStringValue ) ) {
                            jObject.KeyValues.Add( currentIndex.ToString(), currentObjectValue == null ? currentStringValue : currentObjectValue );
                            currentIndex++;
                            currentStringValue = "";
                            currentObjectValue = null;
                        }
                        index += i;
                        return jObject;
                    case ' ':
                    case '\t':
                        if ( inQuotes ) {
                            currentStringValue += json[i];
                        }
                        break;
                    case '\\': {
                            i++;
                            if ( inQuotes ) {
                                switch ( json[i] ) {
                                    case 't':
                                        currentStringValue += '\t';
                                        break;
                                    case 'r':
                                        currentStringValue += '\r';
                                        break;
                                    case 'n':
                                        currentStringValue += '\n';
                                        break;
                                    case 'b':
                                        currentStringValue += '\b';
                                        break;
                                    case 'f':
                                        currentStringValue += '\f';
                                        break;
                                    case 'u':
                                        var s = json.Substring( i + 1, 4 );
                                        currentStringValue += (char)int.Parse( s, System.Globalization.NumberStyles.AllowHexSpecifier );
                                        i += 4;
                                        break;
                                    default:
                                        currentStringValue += json[i];
                                        break;
                                }
                            }
                        }
                        break;
                    default:
                        currentStringValue += json[i];
                        break;
                }

                i++;
            }

            index += i;
            return jObject;
        }

        private string ReadString( string json, ref int index ) {
            var i = 0;
            string value = "";
            while ( i < json.Length ) {
                switch ( json[i] ) {
                    case '"':
                        i++;
                        index += i;
                        return value;
                    default:
                        value += json[i];
                        break;
                }

                i++;
            }

            index += i;
            return value;
        }

        private void ReadJsonTypes( ref string json ) {
            var index = json.IndexOf( "$types" );

            var start = json.IndexOf( "[", index ) + 1;
            var end = json.IndexOf( "]", index );

            var jtemp = json.Substring( start, end - start );

            var splits = jtemp.Split( new string[] { "}," }, StringSplitOptions.RemoveEmptyEntries );
            foreach ( var item in splits ) {
                var temp = item.Trim( ' ', '{' );
                var jType = new JsonType();
                var tid = temp.IndexOf( "\"Typename\"" );

                var assembly = temp.Substring( 0, tid ).Trim( ' ', ',' );
                assembly = assembly.Remove( 0, 11 );

                var typename = temp.Substring( tid );
                typename = typename.Remove( 0, 11 );

                jType.Assembly = assembly.Trim( ' ', '\"' );
                jType.Typename = typename.Trim( '}', ' ', '\"' );

                jsonTypes.Add( jType );
            }

            // -1 & +3 for " and ],
            json = json.Remove( index - 1, end - index + 3 );
        }
    }
}